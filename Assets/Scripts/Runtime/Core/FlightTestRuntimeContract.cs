using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Temporary compatibility bridge for preserved FlightTest scenes.
    ///
    /// The editor builder is still the authoring source of truth. This small
    /// runtime pass exists because Unity projects are often opened with an
    /// older serialized scene after a power loss or a partial builder run. It
    /// repairs only the known EE6 presentation companions; it does not replace
    /// prefabs, move gameplay objects, or touch arbitrary user-authored art.
    /// Remove this bridge once FlightTest has been rebuilt and its serialized
    /// contract is stable across the public repository.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class FlightTestRuntimeContract : MonoBehaviour
    {
        const string FlightTestSceneName = "FlightTest";
        const string RuntimeObjectName = "EE6 FlightTest Runtime Contract";

        static bool repairStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForFlightTest()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()
                || !string.Equals(scene.name, FlightTestSceneName,
                    System.StringComparison.Ordinal))
                return;

            if (FindFirstObjectByType<FlightTestRuntimeContract>())
                return;

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            runtimeObject.AddComponent<FlightTestRuntimeContract>();
        }

        void Awake()
        {
            if (repairStarted)
            {
                enabled = false;
                return;
            }

            repairStarted = true;
            StartCoroutine(RepairAfterSceneSync());
        }

        IEnumerator RepairAfterSceneSync()
        {
            // Let scene-owned Awake/OnEnable methods resolve their normal
            // references first. This bridge should be the last compatibility
            // handoff, never a second initialization path for gameplay.
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()
                || !string.Equals(scene.name, FlightTestSceneName,
                    System.StringComparison.Ordinal))
                yield break;

            int repaired = 0;
            EnergyGate gate = FindFirstObjectByType<EnergyGate>();
            if (gate)
            {
                repaired += EnsureComponent<ProgrammableLaserGate>(gate.gameObject);
                repaired += EnsureComponent<EnergyGatePresentation>(gate.gameObject);
                repaired += DisableGeneratedOutline(gate.GetComponent<LineRenderer>(), 0.5f);
            }

            EnergyKey key = FindFirstObjectByType<EnergyKey>();
            if (key)
            {
                repaired += EnsureComponent<EnergyKeyPresentation>(key.gameObject);
                Transform keyVisual = key.transform.Find("Key Visual");
                if (keyVisual)
                    repaired += DisableGeneratedOutline(
                        keyVisual.GetComponent<LineRenderer>(),
                        0.25f);
            }

            LevelExit exit = FindFirstObjectByType<LevelExit>();
            if (exit)
            {
                repaired += EnsureComponent<ExtractionPortalPresentation>(exit.gameObject);
                repaired += DisableGeneratedOutline(
                    exit.GetComponent<LineRenderer>(),
                    0.45f);
            }

            // Instruction triggers can survive a scene refresh without their
            // display root. Recreate only that transient UI surface; gameplay
            // objective state remains owned by SliceObjectiveDirector.
            if (FindFirstObjectByType<SliceInstructionDisplay>() == null
                && FindFirstObjectByType<SliceInstructionTrigger>())
            {
                GameObject displayObject = new GameObject(
                    "EE6 Runtime Instruction Display");
                displayObject.AddComponent<SliceInstructionDisplay>();
                repaired++;
            }

            if (repaired > 0)
            {
                Debug.Log(
                    $"FlightTest runtime compatibility repaired {repaired} known presentation contract item(s). "
                    + "Run the builder repair menu later to persist the result.",
                    this);
            }
        }

        static int EnsureComponent<T>(GameObject target)
            where T : Component
        {
            if (!target || target.GetComponent<T>())
                return 0;

            target.AddComponent<T>();
            return 1;
        }

        static int DisableGeneratedOutline(LineRenderer line, float halfExtent)
        {
            if (!line || !IsGeneratedSquareOutline(line, halfExtent))
                return 0;

            line.enabled = false;
            return 1;
        }

        static bool IsGeneratedSquareOutline(LineRenderer line, float halfExtent)
        {
            if (!line || line.positionCount != 4)
                return false;

            const float tolerance = 0.02f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = line.GetPosition(i);
                if (Mathf.Abs(Mathf.Abs(point.x) - halfExtent) > tolerance
                    || Mathf.Abs(Mathf.Abs(point.y) - halfExtent) > tolerance)
                    return false;
            }

            return true;
        }

        void OnDestroy()
        {
            repairStarted = false;
        }
    }
}
