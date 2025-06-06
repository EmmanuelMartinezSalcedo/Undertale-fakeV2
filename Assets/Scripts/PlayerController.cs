using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    public float speed = 1f;

    [Header("Tama�o y salud")]
    public float size = 1f;
    public int health = 100;

    [Header("Feedback")]
    public float blinkDuration = 0.1f;
    public int blinkCount = 5;

    private Vector2 targetPosition;
    private SpriteRenderer spriteRenderer;
    private bool isBlinking = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogWarning("No SpriteRenderer found on Player!");
        }
    }

    public void SetTargetPosition(Vector2 worldPosition)
    {
        targetPosition = worldPosition;
    }

    void Update()
    {
        Vector2 currentPos = transform.position;
        Vector2 newPos = Vector2.Lerp(currentPos, targetPosition, speed * Time.deltaTime);
        transform.position = newPos;

        if (Vector2.Distance(newPos, targetPosition) < 0.01f)
        {
            transform.position = targetPosition;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Bullet"))
        {
            Destroy(collision.gameObject); // destruir la bala
            StartCoroutine(BlinkEffect());
            // health -= da�o si lo necesitas
        }
    }

    private IEnumerator BlinkEffect()
    {
        if (isBlinking || spriteRenderer == null) yield break;

        isBlinking = true;
        for (int i = 0; i < blinkCount; i++)
        {
            spriteRenderer.enabled = false;
            yield return new WaitForSeconds(blinkDuration);
            spriteRenderer.enabled = true;
            yield return new WaitForSeconds(blinkDuration);
        }
        isBlinking = false;
    }
}
