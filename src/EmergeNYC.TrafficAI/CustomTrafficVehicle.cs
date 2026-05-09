using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.TrafficAI
{
    /// <summary>
    /// Wraps TSTrafficAI.OnUpdateAI to inject avoidance behavior on top of the
    /// original AI. TSTrafficAI stays enabled — it handles traffic lights, waypoints,
    /// and point reservations. CTV only overrides OnUpdateAI args during avoidance
    /// states; normal operation passes through unchanged. (V18, V19, V22)
    /// </summary>
    public class CustomTrafficVehicle : MonoBehaviour
    {
        public enum State
        {
            Passthrough,   // normal — original OnUpdateAI args pass through unchanged
            SlowDown,      // forward obstacle: reduce throttle, add brake
            PullRight,     // EV siren active beside/behind: add rightward steer bias
            HardStop,      // oncoming EV head-on OR shoulder blocked: full brake
        }

        public State CurrentState { get; private set; } = State.Passthrough;

        // ── Refs ─────────────────────────────────────────────────────────────
        private TSTrafficAI?                      _tsAI;
        private TSNavigation?                     _nav;
        private TSTrafficAI.OnUpdateAIDelegate?   _originalDelegate;
        private Collider?                         _col;
        private float                             _halfLen;

        // ── Per-state data ───────────────────────────────────────────────────
        private float _obstacleProximity;   // 0–1, 1 = very close
        private float _lateralBias;         // current rightward steer offset (lerped)
        private float _targetLateralBias;
        private bool  _evActive;
        private bool  _evHeadOn;            // EV approaching head-on → HardStop regardless of shoulder
        private bool  _shoulderBlocked;     // physical obstacle to the right → can't PullRight
        private float _maxLateralDisplace;

        // ── Tuning ───────────────────────────────────────────────────────────
        private const float ObstacleSlowRange    = 30f;
        private const float ObstacleHardRange    = 8f;
        private const float SirenScanRadius      = 80f;
        private const float SirenHardStopDist    = 30f;
        private const float SirenHardStopDot     = -0.5f;
        private const float PullRightMaxOffset   = 0.5f;    // steer units (TSTrafficAI uses -1..1)
        private const float LateralLerpSpeed     = 1.5f;
        private const float SphereCastRadius     = 1.4f;
        private const float ScanInterval         = 0.1f;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            _tsAI = GetComponent<TSTrafficAI>();
            _nav  = GetComponent<TSNavigation>();
            _col  = GetComponent<Collider>();

            _halfLen = _col != null ? _col.bounds.extents.z : 2.5f;

            if (_tsAI == null)
            {
                TrafficAIPlugin.Log($"[CTV] No TSTrafficAI on {name} — disabling");
                enabled = false;
                return;
            }

            // Defer one frame: TSSimpleCar.Start() wires OnUpdateAI in its own Start().
            // If CTV.Start() ran first we'd capture null and TSSimpleCar would then
            // Delegate.Combine(our wrapper, OnAIUpdate) causing double-invocation.
            StartCoroutine(WrapDelegate());
            StartCoroutine(SirenScanLoop());
            StartCoroutine(ShoulderCheckLoop());
        }

        private void OnEnable()
        {
            // Re-wrap after pool recycle (OnDisable restored the original delegate).
            // Guard: _tsAI null means Start() hasn't run yet — WrapDelegate starts from Start().
            if (_tsAI == null) return;
            StartCoroutine(WrapDelegate());
        }

        private void OnDisable()
        {
            // Restore original delegate so TSTrafficAI drives normally if CTV unloads (V18)
            if (_tsAI != null && _originalDelegate != null)
                _tsAI.OnUpdateAI = _originalDelegate;
        }

        private IEnumerator WrapDelegate()
        {
            yield return null; // wait one frame — guarantees TSSimpleCar.Start() has wired its delegate
            if (_tsAI == null || !enabled) yield break;

            _originalDelegate = _tsAI.OnUpdateAI;
            _tsAI.OnUpdateAI  = OnUpdateAIWrapper;

            if (_originalDelegate == null)
                TrafficAIPlugin.Log($"[CTV] WARNING: original delegate null on {name} after one frame — car won't move");
            else
                TrafficAIPlugin.Log($"[CTV] Wrapped delegate on {name}");
        }

        private void FixedUpdate()
        {
            UpdateMaxLateralDisplace();
            UpdateObstacleState();
            ResolveState();
            LerpLateralBias();
        }

        // ── OnUpdateAI wrapper (V19, V22) ────────────────────────────────────

        private void OnUpdateAIWrapper(float steering, float brake, float throttle, bool isUpSideDown)
        {
            switch (CurrentState)
            {
                case State.Passthrough:
                    _originalDelegate?.Invoke(steering, brake, throttle, isUpSideDown);
                    break;

                case State.SlowDown:
                    // Reduce throttle proportionally, add gentle braking
                    float reducedThrottle = throttle * (1f - _obstacleProximity * 0.7f);
                    float addedBrake      = Mathf.Max(brake, _obstacleProximity * 0.5f);
                    _originalDelegate?.Invoke(steering, addedBrake, reducedThrottle, isUpSideDown);
                    break;

                case State.PullRight:
                    // Add rightward steer bias, reduce speed moderately
                    float pullSteer = Mathf.Clamp(steering + _lateralBias, -1f, 1f);
                    _originalDelegate?.Invoke(pullSteer, brake, throttle * 0.4f, isUpSideDown);
                    break;

                case State.HardStop:
                    _originalDelegate?.Invoke(0f, 1f, 0f, isUpSideDown);
                    break;
            }
        }

        // ── Road edge clamp (T20, V17) ───────────────────────────────────────

        private void UpdateMaxLateralDisplace()
        {
            float edgeDist = PullRightMaxOffset;
            if (NavMesh.FindClosestEdge(transform.position, out NavMeshHit hit, NavMesh.AllAreas))
                edgeDist = Mathf.Max(0f, hit.distance - 0.1f);

            float laneHalf = PullRightMaxOffset;
            if (_nav != null && _nav.lanes != null)
            {
                int lane = _nav.currentLane;
                if (lane >= 0 && lane < _nav.lanes.Length)
                    laneHalf = _nav.lanes[lane].laneWidth / 4f; // quarter-width = right half of right half
            }

            _maxLateralDisplace = Mathf.Clamp(Mathf.Min(edgeDist, laneHalf), 0f, PullRightMaxOffset);
        }

        // ── Forward obstacle (T19, V20) ──────────────────────────────────────

        private void UpdateObstacleState()
        {
            Vector3 origin = transform.position + transform.forward * (_halfLen + 0.5f);
            if (!Physics.SphereCast(origin, SphereCastRadius, transform.forward,
                    out RaycastHit rh, ObstacleSlowRange))
            {
                _obstacleProximity = 0f;
                return;
            }

            // Don't brake for other traffic vehicles — TSTrafficAI already handles car following.
            // Use GetComponentInParent: TSTrafficAI lives on the root but the hit collider
            // is typically on a child mesh object with no TSTrafficAI directly on it.
            if (rh.collider.GetComponentInParent<TSTrafficAI>() != null) { _obstacleProximity = 0f; return; }

            _obstacleProximity = 1f - Mathf.Clamp01((rh.distance - ObstacleHardRange)
                / (ObstacleSlowRange - ObstacleHardRange));
        }

        // ── State resolution ─────────────────────────────────────────────────

        private void ResolveState()
        {
            // EV avoidance overrides obstacle states (V16: siren-gated at source)
            if (_evActive)
            {
                bool hardStop      = _evHeadOn || _shoulderBlocked;
                CurrentState       = hardStop ? State.HardStop : State.PullRight;
                _targetLateralBias = hardStop ? 0f : _maxLateralDisplace;
                return;
            }

            _targetLateralBias = 0f;

            if (_obstacleProximity >= 1f)
                CurrentState = State.HardStop;
            else if (_obstacleProximity > 0f)
                CurrentState = State.SlowDown;
            else
                CurrentState = State.Passthrough;
        }

        private void LerpLateralBias()
        {
            _lateralBias = Mathf.MoveTowards(_lateralBias, _targetLateralBias,
                LateralLerpSpeed * Time.fixedDeltaTime);
        }

        // ── Siren scan coroutine (T21, V16) ──────────────────────────────────

        private IEnumerator SirenScanLoop()
        {
            var wait = new WaitForSeconds(ScanInterval);
            while (true)
            {
                yield return wait;
                ScanForSirenVehicles();
            }
        }

        private void ScanForSirenVehicles()
        {
            var evList = TrafficAPI.GetSirenActiveVehicles(); // V16: siren-gated at source
            _evActive = false;
            Vector3 pos = transform.position;

            foreach (var (evTransform, evVelocity) in evList)
            {
                if (evTransform == null) continue;
                float dist = (evTransform.position - pos).magnitude;
                if (dist > SirenScanRadius) continue;

                _evActive = true;

                // Head-on: EV moving toward us within HardStop range
                if (dist < SirenHardStopDist && evVelocity.sqrMagnitude > 0.1f)
                {
                    float dot = Vector3.Dot(transform.forward, evVelocity.normalized);
                    _evHeadOn = dot < SirenHardStopDot;
                }
                else
                {
                    _evHeadOn = false;
                }
                break;
            }

            if (!_evActive) { _evHeadOn = false; _shoulderBlocked = false; }
        }

        // ── Shoulder obstruction (T22, V17) ──────────────────────────────────

        private IEnumerator ShoulderCheckLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                yield return wait;
                if (_evActive) CheckShoulderObstruction();
            }
        }

        private void CheckShoulderObstruction()
        {
            float checkDist = Mathf.Max(_maxLateralDisplace * 3f, 0.5f); // world-space, not steer units
            Vector3 center  = transform.position + transform.right * checkDist + Vector3.up * 0.5f;
            _shoulderBlocked = Physics.CheckBox(center, new Vector3(0.5f, 0.5f, _halfLen),
                transform.rotation);
        }
    }
}
