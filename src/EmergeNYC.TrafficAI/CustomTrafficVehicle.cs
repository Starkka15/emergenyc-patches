using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using EmergeNYC.ModdingSDK;

namespace EmergeNYC.TrafficAI
{
    /// <summary>
    /// Replaces TSTrafficAI's movement layer with a proper state-machine driver.
    /// Reads TSNavigation for routing, writes RCC_CarControllerV3 inputs for motion.
    /// Injected onto TSTrafficAI vehicles by CTVInjectionPatch (T23).
    /// </summary>
    public class CustomTrafficVehicle : MonoBehaviour
    {
        public enum State
        {
            Cruise,
            SlowDown,
            PullRight,
            HardStop,
            Yield,
            Resume
        }

        // ── State ────────────────────────────────────────────────────────────
        public State CurrentState { get; private set; } = State.Cruise;

        private State _prevObstacleState = State.Cruise;
        private bool  _evActive;          // true = siren-active EV in range (V16)
        private bool  _shoulderBlocked;   // true = OverlapBox found obstacle in shoulder (T22)
        private float _lateralOffset;     // current applied lateral steering bias
        private float _maxLateralDisplace;

        // ── Cached refs ──────────────────────────────────────────────────────
        private RCC_CarControllerV3? _rcc;
        private TSNavigation?        _nav;
        private TSTrafficAI?         _tsAI;
        private TSLaneInfo?          _currentLaneInfo;
        private Collider?            _col;
        private float                _halfLen;

        // ── Tuning ───────────────────────────────────────────────────────────
        private const float ObstacleSlowRange   = 30f;
        private const float ObstacleHardRange   = 8f;
        private const float SirenScanRadius     = 80f;
        private const float SirenHardStopDist   = 30f;
        private const float SirenHardStopDot    = -0.5f;   // approaching head-on threshold
        private const float PullRightMaxOffset  = 2.5f;    // m, max lateral push (clamped by V17)
        private const float LateralLerpSpeed    = 2f;
        private const float SphereCastRadius    = 1.4f;
        private const float CastLayerMask       = ~0;       // refined in T19 if needed
        private const float ScanInterval        = 0.1f;

        // ── AccessTools refs (V19: read TSTrafficAI.enabled only) ───────────

        private void Start()
        {
            _rcc   = GetComponent<RCC_CarControllerV3>();
            _nav   = GetComponent<TSNavigation>();
            _tsAI  = GetComponent<TSTrafficAI>();
            _col   = GetComponent<Collider>();

            if (_col != null)
                _halfLen = _col.bounds.extents.z;
            else
                _halfLen = 2.5f; // fallback

            if (_rcc == null || _nav == null || _tsAI == null)
            {
                TrafficAIPlugin.Log($"[CTV] Missing ref on {name} — disabling CTV");
                enabled = false;
                return;
            }

            // Disable original AI, take over RCC inputs (V18, V19)
            _tsAI.enabled = false;
            _rcc.canControl = false;

            StartCoroutine(SirenScanLoop());
            StartCoroutine(ShoulderCheckLoop());
        }

        private void OnDisable()
        {
            // Re-enable TSTrafficAI only if the GameObject is still active (mod unload path).
            // If the object is being deactivated, TSTrafficAI will also be deactivated — don't touch it. (V18)
            if (gameObject.activeInHierarchy && _tsAI != null)
            {
                _tsAI.enabled = true;
                if (_rcc != null) _rcc.canControl = true;
            }
        }

        private void FixedUpdate()
        {
            if (_rcc == null || _nav == null) return;

            UpdateCurrentLaneInfo();
            UpdateMaxLateralDisplace();  // T20
            UpdateObstacleState();       // T19
            ResolveState();
            ApplyState();
        }

        // ── Lane info ────────────────────────────────────────────────────────

        private void UpdateCurrentLaneInfo()
        {
            if (_nav.lanes == null || _nav.lanes.Length == 0) return;
            int lane = _nav.currentLane;
            if (lane >= 0 && lane < _nav.lanes.Length)
                _currentLaneInfo = _nav.lanes[lane];
        }

        // ── Road edge clamp (T20, V17) ───────────────────────────────────────

        private void UpdateMaxLateralDisplace()
        {
            float edgeDist = float.MaxValue;
            if (NavMesh.FindClosestEdge(transform.position, out NavMeshHit hit, NavMesh.AllAreas))
                edgeDist = Mathf.Max(0f, hit.distance - 0.3f);

            float laneBound = _currentLaneInfo != null ? _currentLaneInfo.laneWidth / 2f : 1.5f;
            _maxLateralDisplace = Mathf.Clamp(Mathf.Min(edgeDist, laneBound), 0f, PullRightMaxOffset);
        }

        // ── Forward obstacle (T19, V20) ──────────────────────────────────────

        private void UpdateObstacleState()
        {
            Vector3 origin = transform.position + transform.forward * (_halfLen + 0.5f);
            bool hit = Physics.SphereCast(origin, SphereCastRadius, transform.forward,
                out RaycastHit rh, ObstacleSlowRange);

            if (!hit)
            {
                if (_prevObstacleState == State.SlowDown || _prevObstacleState == State.HardStop)
                    _prevObstacleState = State.Cruise;
                return;
            }

            if (rh.distance <= ObstacleHardRange)
                _prevObstacleState = State.HardStop;
            else
                _prevObstacleState = State.SlowDown;
        }

        // ── State resolution ─────────────────────────────────────────────────

        private void ResolveState()
        {
            // EV avoidance takes priority when siren is active (V16)
            if (_evActive)
            {
                CurrentState = _shoulderBlocked ? State.HardStop : State.PullRight;
                return;
            }

            // Fall through to obstacle or cruise
            CurrentState = _prevObstacleState;
        }

        // ── State application ────────────────────────────────────────────────

        private void ApplyState()
        {
            switch (CurrentState)
            {
                case State.Cruise:
                    ApplyCruise(1f);
                    ApplyLateralOffset(0f);
                    break;

                case State.SlowDown:
                    // Brake proportional to how close the obstacle is — T19 fills _prevObstacleState
                    // speed already handled by obstacle checks; just coast
                    ApplyCruise(0.4f);
                    ApplyLateralOffset(0f);
                    break;

                case State.HardStop:
                    _rcc!.gasInput   = 0f;
                    _rcc!.brakeInput = 1f;
                    ApplyLateralOffset(0f);
                    break;

                case State.PullRight:
                    ApplyCruise(0.3f);
                    ApplyLateralOffset(_maxLateralDisplace);
                    break;

                case State.Yield:
                    _rcc!.gasInput   = 0f;
                    _rcc!.brakeInput = 0.6f;
                    ApplyLateralOffset(_maxLateralDisplace);
                    break;

                case State.Resume:
                    ApplyCruise(0.6f);
                    ApplyLateralOffset(0f);
                    CurrentState = State.Cruise;
                    break;
            }
        }

        // ── Cruise lane-following (T18, V21) ─────────────────────────────────

        private void ApplyCruise(float throttleScale)
        {
            if (_rcc == null || _nav == null) return;

            // RelativeWaypointPositionOnCar is already in vehicle-local space (V21)
            Vector3 localWP = _nav.RelativeWaypointPositionOnCar;

            // Steer: x-component = lateral offset from car center to waypoint
            float steer = Mathf.Clamp(localWP.x / 5f, -1f, 1f);
            _rcc.steerInput = steer + _lateralOffset;

            // Speed P-controller: currentMaxSpeed is in km/h, RCC.speed is also km/h
            float targetKph = _nav.currentMaxSpeed * throttleScale;
            float delta     = targetKph - _rcc.speed;

            if (delta > 2f)
            {
                _rcc.gasInput   = Mathf.Clamp01(delta / 20f);
                _rcc.brakeInput = 0f;
            }
            else if (delta < -5f)
            {
                _rcc.gasInput   = 0f;
                _rcc.brakeInput = Mathf.Clamp01(-delta / 30f);
            }
            else
            {
                _rcc.gasInput   = 0.05f; // idle creep
                _rcc.brakeInput = 0f;
            }
        }

        // ── Lateral offset application ───────────────────────────────────────

        private void ApplyLateralOffset(float target)
        {
            // Clamp target to road edge limit (V17)
            target = Mathf.Clamp(target, 0f, _maxLateralDisplace);
            _lateralOffset = Mathf.MoveTowards(_lateralOffset, target,
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
                Vector3 toEV  = evTransform.position - pos;
                float   dist  = toEV.magnitude;
                if (dist > SirenScanRadius) continue;

                _evActive = true;

                // Head-on detection: EV moving toward us and in our lane
                if (dist < SirenHardStopDist)
                {
                    Vector3 evDir = evVelocity.sqrMagnitude > 0.1f
                        ? evVelocity.normalized
                        : (pos - evTransform.position).normalized;
                    float dot = Vector3.Dot(transform.forward, evDir);
                    if (dot < SirenHardStopDot)
                    {
                        // Oncoming in our lane — force hard stop
                        _prevObstacleState = State.HardStop;
                        return;
                    }
                }
                break; // nearest siren found, _evActive set
            }
        }

        // ── Shoulder obstruction coroutine (T22, V17) ────────────────────────

        private IEnumerator ShoulderCheckLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                yield return wait;
                CheckShoulderObstruction();
            }
        }

        private void CheckShoulderObstruction()
        {
            if (!_evActive) { _shoulderBlocked = false; return; }

            // Box to the right of the vehicle at target pull offset
            float checkDist   = Mathf.Max(_maxLateralDisplace, 0.5f);
            Vector3 rightCenter = transform.position
                + transform.right * checkDist
                + Vector3.up * 0.5f;
            Vector3 halfExtents = new Vector3(0.5f, 0.5f, _halfLen);

            _shoulderBlocked = Physics.CheckBox(rightCenter, halfExtents, transform.rotation);
        }
    }
}
