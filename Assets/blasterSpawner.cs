using UnityEngine;
using System.Collections;

public class BlasterSpawner : MonoBehaviour
{
    public GameObject laserPrefab;      // Prefab del l�ser
    public Transform firePoint;         // Punto desde donde se disparar� el l�ser
    public float laserSpeed = 10f;      // Velocidad de expansi�n
    public float maxLaserLength = 10f;  // Longitud m�xima del l�ser
    public float chargeDelay = 1f;      // Tiempo antes de disparar
    public float destroyDelay = 1.5f;   // Tiempo despu�s del disparo para destruir todo

    void Start()
    {
        StartCoroutine(FireAfterDelay());
    }

    IEnumerator FireAfterDelay()
    {
        // Espera antes de disparar
        yield return new WaitForSeconds(chargeDelay);

        // Aseg�rate de que firePoint est� asignado
        if (firePoint == null)
        {
            Debug.LogError("BlasterSpawner: Fire Point no asignado.");
            yield break;
        }

        // Instanciar el l�ser en la posici�n y rotaci�n del firePoint
        GameObject laser = Instantiate(laserPrefab, firePoint.position, firePoint.rotation);
        Transform laserTransform = laser.transform;

        // Copiar escala del blaster (o puedes usar firePoint.localScale si prefieres)
        Vector3 baseScale = transform.localScale;

        // Iniciar con longitud 0 en Y (para expandir despu�s)
        laserTransform.localScale = new Vector3(baseScale.x, 0f, baseScale.z);

        float currentLength = 0f;

        // Expandir el l�ser en eje Y
        while (currentLength < maxLaserLength)
        {
            float step = laserSpeed * Time.deltaTime;
            currentLength += step;
            laserTransform.localScale = new Vector3(baseScale.x, currentLength, baseScale.z);
            yield return null;
        }

        // Destruir el l�ser despu�s de un tiempo
        Destroy(laser, 0.5f);

        // Destruir el blaster tambi�n si quieres
        Destroy(gameObject, destroyDelay);
    }
}
