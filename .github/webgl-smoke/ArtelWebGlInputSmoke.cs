using UnityEngine;

public sealed class ArtelWebGlInputSmoke : MonoBehaviour
{
    private void Update()
    {
        // WebGL에서는 이 호출이 SDK 입력 프록시로 교체되면 안 된다.
        if (Input.GetKey(KeyCode.Space)) transform.position += Vector3.right * Time.deltaTime;
    }
}
