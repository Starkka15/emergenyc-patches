using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EmergeNYC.PoliceEnhancement.Components
{
    /// <summary>
    /// Attaches dynamic NavMesh-based patrol routes to NYPD officers.
    /// Finds officers without existing PatrolRoute components and creates
    /// waypoint loops around their spawn position.
    ///
    /// Uses the game's existing PatrolRoute system — we just generate
    /// waypoints dynamically instead of requiring placed scene transforms.
    /// </summary>
    public class OfficerPatrolController : MonoBehaviour
    {
        private float scanInterval = 10f;
        private float lastScan;
        private readonly HashSet<int> processedOfficers = new HashSet<int>();

        // Patrol radius and waypoint count
        private const float PatrolRadius = 30f;
        private const int WaypointCount = 6;
        private const float LingerTime = 3f;

        private void Update()
        {
            if (Time.time - lastScan < scanInterval) return;
            lastScan = Time.time;

            ScanForOfficers();
        }

        private void ScanForOfficers()
        {
            // Find all GameObjects that look like NYPD officers (NPCs, not vehicles)
            var agents = FindObjectsOfType<NavMeshAgent>();
            for (int i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                if (agent == null) continue;

                int id = agent.GetInstanceID();
                if (processedOfficers.Contains(id)) continue;

                // Must be an NYPD officer, not a vehicle
                string name = agent.gameObject.name;
                if (!name.Contains("NYPD") && !name.Contains("Officer") && !name.Contains("Police"))
                    continue;

                // Skip if already has a patrol route
                if (agent.GetComponent<PatrolRoute>() != null)
                {
                    processedOfficers.Add(id);
                    continue;
                }

                // Create dynamic patrol
                SetupPatrol(agent);
                processedOfficers.Add(id);
            }
        }

        private void SetupPatrol(NavMeshAgent agent)
        {
            Vector3 origin = agent.transform.position;

            // Create waypoint holder
            var waypointParent = new GameObject($"PatrolWaypoints_{agent.gameObject.name}");
            waypointParent.transform.SetParent(agent.transform.parent ?? transform);
            waypointParent.transform.position = origin;

            // Generate waypoints in a rough circle around spawn
            var waypoints = new List<Transform>();
            float angleStep = 360f / WaypointCount;

            for (int i = 0; i < WaypointCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * PatrolRadius,
                    0f,
                    Mathf.Sin(angle) * PatrolRadius
                );

                Vector3 candidate = origin + offset;

                // Snap to NavMesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(candidate, out hit, PatrolRadius * 0.5f, NavMesh.AllAreas))
                {
                    var wp = new GameObject($"WP_{i}");
                    wp.transform.SetParent(waypointParent.transform);
                    wp.transform.position = hit.position;
                    waypoints.Add(wp.transform);
                }
            }

            if (waypoints.Count < 2)
            {
                Plugin.Log($"[Patrol] Not enough valid waypoints for {agent.gameObject.name}, skipping");
                Destroy(waypointParent);
                return;
            }

            // Attach PatrolRoute and configure
            var patrol = agent.gameObject.AddComponent<PatrolRoute>();
            patrol.waypoints = new PatrolRoute.Waypoint[waypoints.Count];
            for (int i = 0; i < waypoints.Count; i++)
            {
                patrol.waypoints[i] = new PatrolRoute.Waypoint
                {
                    point = waypoints[i],
                    linger = LingerTime
                };
            }

            patrol.StartPatrol();
            Plugin.Log($"[Patrol] Set up {waypoints.Count}-point patrol for {agent.gameObject.name}");
        }
    }
}
