var sb = new System.Text.StringBuilder();
var pivot = UnityEngine.GameObject.Find("Cannon Pivot");
var body = UnityEngine.GameObject.Find("Cannon Body");
var leftHandle = UnityEngine.GameObject.Find("Left Handle");
var rightHandle = UnityEngine.GameObject.Find("Right Handle");
sb.AppendLine("pivot.pos=" + pivot.transform.position);
sb.AppendLine("body.localPos=" + body.transform.localPosition + " body.worldPos(before)=" + body.transform.position);
sb.AppendLine("leftHandle.worldPos=" + leftHandle.transform.position);
sb.AppendLine("rightHandle.worldPos=" + rightHandle.transform.position);

// simulate the pivot rotating to 45 degrees elevation directly (as CannonAimHandles would)
pivot.transform.localRotation = UnityEngine.Quaternion.Euler(-45f, 0f, 0f);
sb.AppendLine("--- after rotating pivot -45 (45deg elevation) ---");
sb.AppendLine("pivot.pos=" + pivot.transform.position + " (should be UNCHANGED - pivot itself doesn't move, only rotates)");
sb.AppendLine("body.worldPos(after)=" + body.transform.position + " (should still be very close to pivot pos since body's local offset is small)");
sb.AppendLine("leftHandle.worldPos(after)=" + leftHandle.transform.position);
UnityEngine.Debug.Log(sb.ToString());
