using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EmergeNYC.PoliceEnhancement.Components
{
    /// <summary>
    /// Positions NYPD officers around active emergency scenes to form a perimeter.
    /// When a police event activates, finds nearby officers and moves them to
    /// perimeter positions using NavMesh pathfinding.
    ///
    /// Attached to the plugin GameObject in Plugin.Start().
    /// </summary>
    public class PoliceScenePerimeter : MonoBehaviour
    {
        private readonly List<NavMeshAgent> perimeterOfficers = new List<NavMeshAgent>();
        private Vector3 sceneCenter;
        private float perimeterRadius;
        private bool active;

        private const int MaxPerimeterOfficers = 4;
        private const float OfficerSearchRadius = 80f;

        public void SetupPerimeter(Vector3 center, float radius)
        {
            ClearPerimeter();

            sceneCenter = center;
            perimeterRadius = radius;
            active = true;

            // Find nearby NYPD officers with NavMeshAgents
            var agents = FindObjectsOfType<NavMeshAgent>();
            var candidates = new List<NavMeshAgent>();

            for (int i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (agent == null || !agent.isOnNavMesh) continue;

                string name = agent.gameObject.name;
                if (!name.Contains("NYPD") && !name.Contains("Officer") && !name.Contains("Police"))
                    continue;

                float dist = Vector3.Distance(agent.transform.position, center);
                if (dist < OfficerSearchRadius)
                    candidates.Add(agent);
            }

            // Sort by distance and take closest
            candidates.Sort((a, b) =>
                Vector3.Distance(a.transform.position, center)
                    .CompareTo(Vector3.Distance(b.transform.position, center)));

            int count = Mathf.Min(candidates.Count, MaxPerimeterOfficers);
            float angleStep = 360f / Mathf.Max(count, 1);

            for (int i = 0; i < count; i++)
            {
                var agent = candidates[i];

                // Stop any existing patrol
                var patrol = agent.GetComponent<PatrolRoute>();
                if (patrol != null)
                    patrol.StopPatrol();

                // Calculate perimeter position
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 target = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius
                );

                // Snap to NavMesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(target, out hit, radius * 0.5f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                    perimeterOfficers.Add(agent);
                }
            }

            Plugin.Log($"[Perimeter] Set up {perimeterOfficers.Count} officers around scene at {center}");
        }

        public void ClearPerimeter()
        {
            if (!active) return;

            // Resume patrol for perimeter officers
            for (int i = 0; i < perimeterOfficers.Count; i++)
            {
                var agent = perimeterOfficers[i];
                if (agent == null) continue;

                var patrol = agent.GetComponent<PatrolRoute>();
                if (patrol != null)
                    patrol.StartPatrol();
            }

            perimeterOfficers.Clear();
            active = false;
            Plugin.Log("[Perimeter] Cleared perimeter, officers resuming patrol");
        }
    }
}
