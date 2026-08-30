var cannon = UnityEngine.GameObject.Find("Cannon Body");
var cam = UnityEngine.Camera.main;
if (cannon != null)
{
    cam.transform.position = cannon.transform.position + new UnityEngine.Vector3(2.5f, 2.0f, -3.5f);
    cam.transform.LookAt(cannon.transform.position + UnityEngine.Vector3.up * 0.3f);
}
UnityEngine.Debug.Log("cannon.pos=" + (cannon != null ? cannon.transform.position.ToString() : "not found"));
