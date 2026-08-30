var cannon = UnityEngine.GameObject.Find("Cannon Body");
var baseObj = UnityEngine.GameObject.Find("Cannon Base");
var sb = new System.Text.StringBuilder();
sb.AppendLine("cannon=" + (cannon != null ? cannon.transform.position.ToString() + " scale=" + cannon.transform.lossyScale : "NULL"));
sb.AppendLine("base=" + (baseObj != null ? baseObj.transform.position.ToString() : "NULL"));
var cam = UnityEngine.Camera.main;
if (cannon != null)
{
    cam.transform.position = cannon.transform.position + new UnityEngine.Vector3(3f, 2.5f, -4f);
    cam.transform.rotation = UnityEngine.Quaternion.LookRotation((cannon.transform.position + UnityEngine.Vector3.up*0.5f) - cam.transform.position);
}
UnityEngine.Debug.Log(sb.ToString());
