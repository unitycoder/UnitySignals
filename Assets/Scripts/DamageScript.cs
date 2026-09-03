using UnityEngine;

public class DamageScript : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var health = GetComponent<Health>();
            if (health != null)
            {
                health.TakeDamage(10);
                Debug.Log("Damage taken: 10", this);
            }
        }
    }
}
