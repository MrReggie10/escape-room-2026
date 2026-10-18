using UnityEngine;
using System.Collections;

public class DinoMovement : MonoBehaviour
{
    public Transform transform;
    public Rigidbody2D rb;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey("up"))
        {
            rb.linearVelocity = new Vector2(0, 5);
        }
    }

    void FixedUpdate()
    {

    }
}
