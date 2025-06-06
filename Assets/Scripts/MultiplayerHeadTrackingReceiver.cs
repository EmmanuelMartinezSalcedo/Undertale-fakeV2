using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

public class MultiplayerHeadTrackingReceiver : MonoBehaviour
{
    [Header("Configuración de Red")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 12345;

    [Header("Elementos de escena")]
    public SpriteRenderer backgroundSpriteRenderer;
    public MultiplayerPlayerController head;
    public Camera mainCamera;

    [Header("Configuración")]
    public float smoothingFactor = 0.8f;

    [Header("Status")]
    public bool isConnected = false;
    public bool isReceivingData = false;

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread receiveThread;

    private Texture2D webcamTexture;
    private HeadData latestHeadData;
    private bool hasNewData = false;
    private Vector2 smoothedPosition;
    private Vector2 targetPosition;

    private StringBuilder messageBuffer = new StringBuilder();

    private Process pythonProcess;

    void Start()
    {
        StartPythonConnection();
        webcamTexture = new Texture2D(2, 2);
        Invoke(nameof(ConnectToServer), 2f);
    }

    public void StartPythonConnection()
    {
        string relativePath = "Scripts/Python/HeadTracking/HeadTracker.exe";
        string exePath = Path.Combine(Application.dataPath, relativePath);

        if (File.Exists(exePath))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exePath;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            string currentPath = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            string exeDir = Path.GetDirectoryName(exePath);
            startInfo.EnvironmentVariables["PATH"] = exeDir + ";" + currentPath;

            pythonProcess = new Process();
            pythonProcess.StartInfo = startInfo;
            pythonProcess.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                    UnityEngine.Debug.Log("[PYTHON] " + args.Data);
            };
            pythonProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                    UnityEngine.Debug.LogError("[PYTHON ERROR] " + args.Data);
            };

            pythonProcess.Start();
            pythonProcess.BeginOutputReadLine();
            pythonProcess.BeginErrorReadLine();

            UnityEngine.Debug.Log("Python process started");
        }
        else
        {
            UnityEngine.Debug.LogError("HeadTracker.exe not found at: " + exePath);
        }
    }

    void ConnectToServer()
    {
        int maxRetries = 10;
        int currentRetry = 0;

        while (currentRetry < maxRetries)
        {
            try
            {
                UnityEngine.Debug.Log($"Connection attempt {currentRetry + 1}/{maxRetries}");

                tcpClient = new TcpClient(serverIP, serverPort);
                stream = tcpClient.GetStream();
                isConnected = true;

                UnityEngine.Debug.Log("Connected to Python server");

                // PASO 1: Esperar que Python diga que está listo
                if (WaitForMessage("PYTHON_READY", 30f))
                {
                    UnityEngine.Debug.Log("Received PYTHON_READY");

                    // PASO 2: Unity confirma que está listo
                    SendCustomMessage("UNITY_READY");
                    UnityEngine.Debug.Log("Sent UNITY_READY");

                    // PASO 3: Esperar confirmación para comenzar transmisión
                    if (WaitForMessage("START_TRANSMISSION", 10f))
                    {
                        UnityEngine.Debug.Log("Received START_TRANSMISSION - Beginning data reception");
                        isReceivingData = true;

                        // Iniciar hilo de recepción de datos
                        receiveThread = new Thread(ReceiveData);
                        receiveThread.Start();
                        return; // ¡Éxito!
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("Failed to receive START_TRANSMISSION");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError("Failed to receive PYTHON_READY");
                }

                // Si llegamos aquí, algo falló
                tcpClient.Close();
                isConnected = false;

            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Connection attempt {currentRetry + 1} failed: {e.Message}");
            }

            currentRetry++;
            if (currentRetry < maxRetries)
            {
                UnityEngine.Debug.Log("Retrying in 2 seconds...");
                Thread.Sleep(2000);
            }
        }

        UnityEngine.Debug.LogError("Failed to connect after all attempts");
    }

    private bool WaitForMessage(string expectedMessage, float timeoutSeconds)
    {
        float startTime = Time.time;
        byte[] buffer = new byte[1024];

        while ((Time.time - startTime) < timeoutSeconds)
        {
            if (stream.DataAvailable)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    UnityEngine.Debug.Log($"Received: '{receivedMessage}', Expected: '{expectedMessage}'");

                    if (receivedMessage == expectedMessage)
                    {
                        return true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"Unexpected message: {receivedMessage}");
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Error reading message: {e.Message}");
                    return false;
                }
            }
            Thread.Sleep(100); // Evitar busy waiting
        }

        UnityEngine.Debug.LogError($"Timeout waiting for '{expectedMessage}'");
        return false;
    }

    private void SendCustomMessage(string message)
    {
        try
        {
            string fullMessage = message + "\n";
            byte[] data = Encoding.UTF8.GetBytes(fullMessage);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Error sending message: {e.Message}");
            throw;
        }
    }

    void ReceiveData()
    {
        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
        string incompleteData = "";

        UnityEngine.Debug.Log("Data reception thread started");

        while (isReceivingData && isConnected)
        {
            try
            {
                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string newData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        incompleteData += newData;

                        // Procesar mensajes completos usando protocolo "length:data"
                        while (true)
                        {
                            int colonIndex = incompleteData.IndexOf(':');
                            if (colonIndex == -1) break; // No hay mensaje completo aún

                            string lengthStr = incompleteData.Substring(0, colonIndex);
                            if (int.TryParse(lengthStr, out int expectedLength))
                            {
                                int totalMessageLength = colonIndex + 1 + expectedLength;
                                if (incompleteData.Length >= totalMessageLength)
                                {
                                    // Tenemos un mensaje completo
                                    string jsonData = incompleteData.Substring(colonIndex + 1, expectedLength);
                                    ProcessReceivedData(jsonData);

                                    // Remover el mensaje procesado del buffer
                                    incompleteData = incompleteData.Substring(totalMessageLength);
                                }
                                else
                                {
                                    // Mensaje incompleto, esperar más datos
                                    break;
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogError("Invalid message format - could not parse length");
                                incompleteData = ""; // Limpiar buffer corrupto
                                break;
                            }
                        }
                    }
                }
                Thread.Sleep(1); // Mantener tu timing original
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error recibiendo datos: {e.Message}");
                break;
            }
        }

        UnityEngine.Debug.Log("Data reception thread ended");
    }

    void ProcessReceivedData(string data)
    {
        messageBuffer.Append(data);
        string bufferContent = messageBuffer.ToString();

        while (true)
        {
            int colonIndex = bufferContent.IndexOf(':');
            if (colonIndex == -1) break;

            string lengthStr = bufferContent.Substring(0, colonIndex);
            if (!int.TryParse(lengthStr, out int messageLength)) break;

            int totalMessageLength = colonIndex + 1 + messageLength;
            if (bufferContent.Length < totalMessageLength) break;

            string jsonMessage = bufferContent.Substring(colonIndex + 1, messageLength);
            bufferContent = bufferContent.Substring(totalMessageLength);

            try
            {
                HeadData headData = JsonConvert.DeserializeObject<HeadData>(jsonMessage);
                latestHeadData = headData;
                hasNewData = true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error parseando JSON: {e.Message}");
            }
        }

        messageBuffer.Clear();
        messageBuffer.Append(bufferContent);
    }

    void Update()
    {
        if (hasNewData && latestHeadData != null)
        {
            ProcessHeadData(latestHeadData);
            hasNewData = false;
        }

        if (head != null && backgroundSpriteRenderer != null && backgroundSpriteRenderer.sprite != null)
        {
            smoothedPosition = targetPosition;

            Vector3 spriteScale = backgroundSpriteRenderer.transform.localScale;
            float spriteWidth = backgroundSpriteRenderer.sprite.bounds.size.x * spriteScale.x;
            float spriteHeight = backgroundSpriteRenderer.sprite.bounds.size.y * spriteScale.y;

            float clampedX = Mathf.Clamp(smoothedPosition.x, 0f, 1f);
            float clampedY = Mathf.Clamp(1f - smoothedPosition.y, 0f, 1f);

            float worldX = backgroundSpriteRenderer.transform.position.x - spriteWidth / 2f + clampedX * spriteWidth;
            float worldY = backgroundSpriteRenderer.transform.position.y - spriteHeight / 2f + clampedY * spriteHeight;

            Vector3 worldPos = new Vector3(worldX, worldY, 0f);
            head.SetTargetPosition(worldPos);
        }
    }

    void ProcessHeadData(HeadData data)
    {
        if (!string.IsNullOrEmpty(data.frame_data) && backgroundSpriteRenderer != null)
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(data.frame_data);
                webcamTexture.LoadImage(imageBytes);

                Sprite newSprite = Sprite.Create(webcamTexture,
                    new Rect(0, 0, webcamTexture.width, webcamTexture.height),
                    new Vector2(0.5f, 0.5f));
                backgroundSpriteRenderer.sprite = newSprite;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error cargando imagen: {e.Message}");
            }
        }

        if (data.head_position != null)
        {
            targetPosition = new Vector2(
                data.head_position.normalized_x,
                data.head_position.normalized_y
            );
        }
    }

    void OnApplicationQuit() => Disconnect();

    void OnDestroy()
    {
        Disconnect();
        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            pythonProcess.Kill();
            pythonProcess.Dispose();
            pythonProcess = null;
        }
    }
    void Disconnect()
    {
        isReceivingData = false;
        isConnected = false;

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            if (receiveThread.IsAlive)
                receiveThread.Abort();
        }

        stream?.Close();
        tcpClient?.Close();

        if (pythonProcess != null && !pythonProcess.HasExited)
        {
            pythonProcess.Kill();
            pythonProcess.Dispose();
        }

        if (webcamTexture != null)
        {
            DestroyImmediate(webcamTexture);
        }
    }
}