var player = UnityEngine.GameObject.Find("PlayerPhysics");
var pivot = UnityEngine.GameObject.Find("Cannon Pivot");
var sb = new System.Text.StringBuilder();
sb.AppendLine("pivot found=" + (pivot != null));
if (pivot != null)
{
    sb.AppendLine("pivot.pos=" + pivot.transform.position);
    player.transform.position = pivot.transform.position + new UnityEngine.Vector3(2.2f, 1.6f, -2.8f);
    player.transform.rotation = UnityEngine.Quaternion.LookRotation(pivot.transform.position - player.transform.position);
}
UnityEngine.Debug.Log(sb.ToString());
