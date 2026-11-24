using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CpcPrototype;
using UnityEngine;

public static class GrappleDirectionChecks
{
    public static async Task<string> Execute()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<SurvivalPlayer>();
        var grapple = player.Grapple;
        var controller = player.GetComponent<CharacterController>();
        var anchor = new GameObject("Direction Check Anchor").AddComponent<GrappleAnchor>();
        anchor.transform.position = new Vector3(0f, .6f, 0f);
        var report = new StringBuilder();
        bool sniperEnabled = player.GrappleSniper.enabled;
        Vector3 originalCameraOffset = player.DepthCameraOffset;
        player.GrappleSniper.enabled = false;

        void Check(bool condition, string label)
        {
            report.AppendLine((condition ? "PASS " : "FAIL ") + label);
            if (!condition) throw new InvalidOperationException(label);
        }

        async Task<Vector3> Swing(float x, bool blocked)
        {
            grapple.Release();
            controller.enabled = false;
            player.transform.position = new Vector3(x, .01f, .35f);
            controller.enabled = true;
            player.ResetMotion();
            Physics.SyncTransforms();
            await Task.Delay(100);
            player.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Vector3 start = player.transform.position;
            Vector3 radial = start - anchor.Position;
            Transform cameraTransform = player.View.transform;
            Vector3 screenRadial = cameraTransform.InverseTransformDirection(radial);
            float downSign = -Mathf.Sign(screenRadial.x);
            Vector3 screenRight = cameraTransform.right;
            Vector3 screenUp = cameraTransform.up;
            Vector3 lateral = Vector3.Cross(Vector3.up, radial).normalized;
            Vector3 towardBulb = Vector3.ProjectOnPlane(-radial, Vector3.up).normalized;
            string label = blocked ? "Blocked blue arc" : x < 0f ? "Left start" : "Right start";
            if (player.DepthCameraOffset != originalCameraOffset) label += " with camera on the opposite side";
            Check(grapple.TryBegin(anchor), label + " starts");
            Vector3 midpoint = start;
            float closest = float.PositiveInfinity;
            float lowestSide = 0f;
            float highestSide = 0f;
            float sidewaysDrift = 0f;
            float forwardTravel = 0f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (grapple.IsTravelling && Time.realtimeSinceStartup < deadline)
            {
                Vector3 offset = player.transform.position - anchor.Position;
                float side = downSign * (screenRadial.x * Vector3.Dot(offset, screenUp) -
                    screenRadial.y * Vector3.Dot(offset, screenRight));
                lowestSide = Mathf.Min(lowestSide, side);
                highestSide = Mathf.Max(highestSide, side);
                sidewaysDrift = Mathf.Max(sidewaysDrift, Mathf.Abs(Vector3.Dot(offset, lateral)));
                forwardTravel = Mathf.Max(forwardTravel, Vector3.Dot(player.transform.position - start, towardBulb));
                float midpointError = Mathf.Abs(Vector3.Dot(offset, radial));
                if (midpointError < closest)
                {
                    midpoint = player.transform.position;
                    closest = midpointError;
                }
                await Task.Delay(15);
            }

            Check(lowestSide > -.002f && highestSide > .01f, label + " stays on the blue side of the starting line");
            Check(sidewaysDrift < .005f && forwardTravel > .1f,
                $"{label} travels toward the bulb without sideways drift in plan view (drift {sidewaysDrift:F4}, forward {forwardTravel:F4})");
            if (blocked)
                Check(grapple.Phase == GrapplePhase.Idle && !grapple.Rope.enabled, "Obstacle cancels the swing without selecting the red arc");
            else
                Check(grapple.Phase == GrapplePhase.Holding && Vector3.Distance(player.transform.position, anchor.Position - .5f * radial) < .015f,
                    label + " reaches the opposite side at half radius");
            return midpoint;
        }

        GameObject obstacle = null;
        try
        {
            Vector3 midpoint = await Swing(-.7f, false);
            await Swing(.7f, false);
            obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = midpoint + controller.bounds.center - player.transform.position;
            // Align the wall with the heading so a physical slide along its face cannot skew the path measurement.
            obstacle.transform.rotation = Quaternion.LookRotation(new Vector3(.7f, 0f, -.35f));
            obstacle.transform.localScale = Vector3.one * .24f;
            await Swing(-.7f, true);
            UnityEngine.Object.Destroy(obstacle);
            player.DepthCameraOffset = new Vector3(originalCameraOffset.x, originalCameraOffset.y, -originalCameraOffset.z);
            await Swing(-.7f, false);
            await Swing(.7f, false);
            return report.ToString();
        }
        finally
        {
            grapple.Release();
            player.GrappleSniper.enabled = sniperEnabled;
            player.DepthCameraOffset = originalCameraOffset;
            UnityEngine.Object.Destroy(anchor.gameObject);
            if (obstacle != null) UnityEngine.Object.Destroy(obstacle);
            Directory.CreateDirectory("Tools/GrappleSniper/Bug010");
            File.WriteAllText("Tools/GrappleSniper/Bug010/DirectionChecks.txt", report.ToString());
        }
    }
}
