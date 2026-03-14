using System.IO;
using UnityEngine;

namespace EmergeNYC.EMSEnhancement.Components
{
    /// <summary>
    /// Adds condition-based vital sign deterioration to EMS patients.
    /// Attached via Harmony patch on SituationManager.Start().
    ///
    /// The native EMS system has vital sign components (VascularSystem, RespiratorySystem,
    /// EpidermalSystem, Awareness) but they're static — values never change unless a
    /// treatment modifier is applied through the native treatment UI (H key).
    ///
    /// This component makes patients deteriorate over time based on their condition,
    /// creating urgency. The native treatment system pushes vitals back up; we push
    /// them down. Untreated patients get worse and die.
    ///
    /// Condition deterioration profiles:
    /// - Cardiac: pulse/BP crash to zero, O2 drops, temp falls
    /// - GSW: tachycardia → hemorrhagic shock, BP drops, O2 slowly falls
    /// - Overdose: respiratory depression, O2 crashes, bradycardia
    /// - Respiratory: O2 drops fast, compensatory tachycardia then collapse
    /// - Trauma (fall/accident): mild — slightly elevated vitals, slow decline
    /// </summary>
    public class EMSPatientEnhancer : MonoBehaviour
    {
        // Native vital system references
        private VascularSystem? vascular;
        private RespiratorySystem? respiratory;
        private EpidermalSystem? epidermal;
        private Awareness? awareness;
        private SituationManager? situation;

        // Condition
        private PatientCondition condition = PatientCondition.None;
        private bool isDead;

        // Timing
        private float timeSinceStart;
        private float lastVitalUpdate;
        private const float VitalUpdateInterval = 1f;

        // Deterioration rates (per second, applied once per VitalUpdateInterval)
        private const float PulseRate = 0.5f;
        private const float BPRate = 0.3f;
        private const float O2Rate = 0.15f;
        private const float RespRate = 0.1f;
        private const float TempRate = 0.005f;

        public enum PatientCondition
        {
            None,
            Cardiac,
            GSW,
            Overdose,
            Respiratory,
            Fall,
            CarAccident
        }

        public void Initialize()
        {
            vascular = GetComponent<VascularSystem>() ?? GetComponentInChildren<VascularSystem>();
            respiratory = GetComponent<RespiratorySystem>() ?? GetComponentInChildren<RespiratorySystem>();
            epidermal = GetComponent<EpidermalSystem>() ?? GetComponentInChildren<EpidermalSystem>();
            awareness = GetComponent<Awareness>() ?? GetComponentInChildren<Awareness>();
            situation = GetComponent<SituationManager>();

            if (situation != null)
            {
                if (situation.cardiac) condition = PatientCondition.Cardiac;
                else if (situation.gsw) condition = PatientCondition.GSW;
                else if (situation.overdose) condition = PatientCondition.Overdose;
                else if (situation.respiratory) condition = PatientCondition.Respiratory;
                else if (situation.fall) condition = PatientCondition.Fall;
                else if (situation.caraccident) condition = PatientCondition.CarAccident;
            }

            // Set condition-appropriate starting vitals.
            // The native condition GameObjects may also set vitals — our values provide
            // a realistic baseline if they don't.
            SetInitialVitals();

            Plugin.Log($"[Deterioration] Patient {gameObject.name}: condition={condition}" +
                (vascular != null ? $" pulse={vascular.p_pulse:F0} BP={vascular.p_bloodPreasure.x:F0}/{vascular.p_bloodPreasure.y:F0}" : "") +
                (respiratory != null ? $" O2={respiratory.O2Stat:F0} resp={respiratory.Respirations:F0}" : ""));
        }

        private void SetInitialVitals()
        {
            switch (condition)
            {
                case PatientCondition.Cardiac:
                    SetVitals(0f, 0f, 0f, 85f, 0f, 97.5f);
                    SetAwareness("Unresponsive", "Unconscious", "Fixed");
                    break;
                case PatientCondition.GSW:
                    SetVitals(118f, 90f, 55f, 93f, 22f, 98.2f);
                    SetAwareness("Pain", "Altered", "PERRL");
                    break;
                case PatientCondition.Overdose:
                    SetVitals(48f, 95f, 60f, 82f, 5f, 97f);
                    SetAwareness("Unresponsive", "Unconscious", "Constricted");
                    break;
                case PatientCondition.Respiratory:
                    SetVitals(110f, 135f, 85f, 78f, 28f, 98.6f);
                    SetAwareness("Verbal", "Altered", "PERRL");
                    break;
                case PatientCondition.Fall:
                case PatientCondition.CarAccident:
                    SetVitals(95f, 140f, 88f, 96f, 18f, 98.6f);
                    SetAwareness("Pain", "Conscious", "PERRL");
                    break;
            }
        }

        private void SetVitals(float pulse, float bpSys, float bpDia, float o2, float resp, float temp)
        {
            if (vascular != null)
            {
                vascular.p_pulse = pulse;
                vascular.p_bloodPreasure = new Vector2(bpSys, bpDia);
            }
            if (respiratory != null)
            {
                respiratory.O2Stat = o2;
                respiratory.Respirations = resp;
            }
            if (epidermal != null)
            {
                epidermal.temperatureSkin = temp;
                UpdateTempDescription();
            }
        }

        private void SetAwareness(string responsiveness, string consciousness, string pupils)
        {
            if (awareness == null) return;
            awareness.Responsiveness = responsiveness;
            awareness.Conciousness = consciousness;
            awareness.Pupils = pupils;
        }

        private void Update()
        {
            if (isDead || condition == PatientCondition.None)
                return;

            timeSinceStart += Time.deltaTime;

            if (Time.time - lastVitalUpdate < VitalUpdateInterval)
                return;
            lastVitalUpdate = Time.time;

            switch (condition)
            {
                case PatientCondition.Cardiac:   TickCardiac(); break;
                case PatientCondition.GSW:        TickGSW(); break;
                case PatientCondition.Overdose:   TickOverdose(); break;
                case PatientCondition.Respiratory: TickRespiratory(); break;
                case PatientCondition.Fall:
                case PatientCondition.CarAccident: TickTrauma(); break;
            }

            UpdateAwareness();
            UpdateTempDescription();
            CheckDeath();
        }

        // =================================================================
        // Condition deterioration — native treatments counteract these
        // =================================================================

        private void TickCardiac()
        {
            if (vascular == null || respiratory == null) return;

            // Pulse and BP crash toward zero
            vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, 0f, PulseRate * 2f);
            vascular.p_bloodPreasure = Vector2.MoveTowards(
                vascular.p_bloodPreasure, Vector2.zero, BPRate * 2f);
            // O2 drops without circulation
            respiratory.O2Stat = Mathf.MoveTowards(respiratory.O2Stat, 0f, O2Rate * 2f);
            respiratory.Respirations = Mathf.MoveTowards(respiratory.Respirations, 0f, RespRate * 3f);
            // Temp slowly falls
            if (epidermal != null)
                epidermal.temperatureSkin = Mathf.MoveTowards(epidermal.temperatureSkin, 95f, TempRate);
        }

        private void TickGSW()
        {
            if (vascular == null || respiratory == null) return;

            // Early: compensatory tachycardia. Late: decompensated bradycardia.
            float pulseTarget = timeSinceStart < 120f ? 140f : 40f;
            vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, pulseTarget, PulseRate);
            // Progressive hypotension from blood loss
            vascular.p_bloodPreasure = Vector2.MoveTowards(
                vascular.p_bloodPreasure, new Vector2(50f, 30f), BPRate);
            // Slow O2 decline
            respiratory.O2Stat = Mathf.MoveTowards(respiratory.O2Stat, 70f, O2Rate * 0.5f);
            // Temp drops from hemorrhagic shock
            if (epidermal != null)
                epidermal.temperatureSkin = Mathf.MoveTowards(epidermal.temperatureSkin, 96f, TempRate);
        }

        private void TickOverdose()
        {
            if (vascular == null || respiratory == null) return;

            // Respiratory depression: respirations and O2 crash
            respiratory.Respirations = Mathf.MoveTowards(respiratory.Respirations, 0f, RespRate);
            respiratory.O2Stat = Mathf.MoveTowards(respiratory.O2Stat, 0f, O2Rate * 1.5f);
            // Bradycardia
            vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, 30f, PulseRate * 0.5f);
            vascular.p_bloodPreasure = Vector2.MoveTowards(
                vascular.p_bloodPreasure, new Vector2(70f, 45f), BPRate * 0.5f);
            // Temp drops
            if (epidermal != null)
                epidermal.temperatureSkin = Mathf.MoveTowards(epidermal.temperatureSkin, 96f, TempRate);
        }

        private void TickRespiratory()
        {
            if (vascular == null || respiratory == null) return;

            // O2 drops fast
            respiratory.O2Stat = Mathf.MoveTowards(respiratory.O2Stat, 0f, O2Rate * 2f);
            // Compensatory tachycardia early, collapse late
            if (respiratory.O2Stat > 60f)
                vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, 150f, PulseRate * 0.3f);
            else
                vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, 30f, PulseRate);
        }

        private void TickTrauma()
        {
            if (vascular == null) return;

            // Mild — mostly pain-driven elevation, very slow decline
            vascular.p_pulse = Mathf.MoveTowards(vascular.p_pulse, 98f, PulseRate * 0.1f);
        }

        // =================================================================
        // Awareness tracks vital severity
        // =================================================================

        private void UpdateAwareness()
        {
            if (awareness == null || vascular == null || respiratory == null) return;

            float o2 = respiratory.O2Stat;
            float pulse = vascular.p_pulse;
            float bpSys = vascular.p_bloodPreasure.x;

            if (o2 < 40f || pulse <= 0f || bpSys < 30f)
                SetAwareness("Unresponsive", "Unconscious",
                    condition == PatientCondition.Cardiac ? "Fixed" :
                    condition == PatientCondition.Overdose ? "Constricted" : awareness.Pupils);
            else if (o2 < 60f || pulse < 40f || bpSys < 60f)
                SetAwareness("Pain", "Unconscious", awareness.Pupils);
            else if (o2 < 80f || pulse < 50f || bpSys < 75f)
                SetAwareness("Verbal", "Altered", awareness.Pupils);
        }

        private void UpdateTempDescription()
        {
            if (epidermal == null) return;
            float t = epidermal.temperatureSkin;
            epidermal.temperatureSkinDescription =
                t >= 100.4f ? "Febrile" :
                t >= 98f    ? "Normal" :
                t >= 96f    ? "Cool" :
                t >= 93f    ? "Cold" : "Hypothermic";
        }

        private void CheckDeath()
        {
            if (vascular == null || respiratory == null) return;

            if (vascular.p_pulse <= 0f && respiratory.O2Stat <= 0f)
            {
                isDead = true;
                SetAwareness("Unresponsive", "Unconscious", "Fixed");
                Plugin.Log($"[Deterioration] Patient {gameObject.name} has died (condition={condition}, t={timeSinceStart:F0}s)");
            }

            // Cardiac: death after 5 min with no intervention
            if (condition == PatientCondition.Cardiac && timeSinceStart > 300f && vascular.p_pulse <= 0f)
            {
                isDead = true;
                SetAwareness("Unresponsive", "Unconscious", "Fixed");
                Plugin.Log($"[Deterioration] Cardiac patient {gameObject.name} died — no intervention within 5 min");
            }
        }
    }
}
