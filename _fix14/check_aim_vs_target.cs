var sb = new System.Text.StringBuilder();
var bow = UnityEngine.GameObject.Find("Bow Stand");
var game = UnityEngine.Object.FindFirstObjectByType<AILearningEcosystem.Hub.ArcheryProjectileGame>();
var t = typeof(AILearningEcosystem.Hub.ArcheryProjectileGame);
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var targetFace = (UnityEngine.Transform)t.GetField("_targetFace", flags).GetValue(game);
sb.AppendLine("bow.pos=" + bow.transform.position + " bow.forward=" + bow.transform.forward);
sb.AppendLine("targetFace=" + (targetFace != null ? targetFace.name + " pos=" + targetFace.position : "NULL"));
if (targetFace != null)
{
    var toTarget = (targetFace.position - bow.transform.position);
    toTarget.y = 0;
    toTarget.Normalize();
    sb.AppendLine("direction to target (flat)=" + toTarget);
    var bowForwardFlat = new UnityEngine.Vector3(bow.transform.forward.x, 0, bow.transform.forward.z).normalized;
    var angle = UnityEngine.Vector3.SignedAngle(bowForwardFlat, toTarget, UnityEngine.Vector3.up);
    sb.AppendLine("angle needed (positive = need to rotate this many degrees, sign per Unity convention)=" + angle);
}
UnityEngine.Debug.Log(sb.ToString());
