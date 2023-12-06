using System.IO;
using System.Net.Sockets;
using System.Net;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System;
using System.Xml;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;

public class QrCodeRecenter : MonoBehaviour {

    [SerializeField]
    private ARSession session;
    [SerializeField]
    private XROrigin sessionOrigin;
    [SerializeField]
    private ARCameraManager cameraManager;
    [SerializeField]
    private TargetHandler targetHandler;

    private Texture2D m_Texture;
    private bool scanningEnabled = false;
    private byte[] mess ;
    private byte[] head;
    private byte[] imagebytes;
    private float[] kf=new float[0];
    private int lenoflastjpg=0;
    Socket client;
    // Use this for initialization

    private void Update()
    {
        if (kf.Length!=0)
        {
            SetPos(kf);
            kf=new float[0];
        }
    }

    private void ConnectAsync()
    {
        client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPEndPoint ipEndPoint = new(IPAddress.Parse("192.168.0.144"), 8888);
        client.BeginConnect(ipEndPoint, ConnectCallBack, client); 
          
    }
    
    private void Sendcallback(IAsyncResult ar)
    {
        try
        {
            Socket socket = (Socket)ar.AsyncState;
            int count = socket.EndSend(ar);
        }
        catch (SocketException e)
        {
            Debug.Log("socket send erro:" + e.ToString());
        }
    }

    private void ConnectCallBack(IAsyncResult ar)
    {
        if (client.Connected)
        {
            Debug.Log("connected success !");
            mess = new byte[1024];
            Sendimage(imagebytes);
        }
        else
        {
            Debug.Log("not connected");
        }
    }

    public byte[] Getimghead(int len)
    {
        int lendiv = len / 1024;
        int mod = len % 1024;
        List<byte> byteSource = new();
        byteSource.AddRange(BitConverter.GetBytes(lendiv));
        byteSource.AddRange(BitConverter.GetBytes(mod));
        /*
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.position.x));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.position.y));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.position.z));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.rotation.w));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.rotation.x));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.rotation.y));
        byteSource.AddRange(BitConverter.GetBytes(cameraManager.transform.rotation.z));
        */
        return byteSource.ToArray();
    }

    public void Sendimage(byte[] imgbyt)
    {
        Debug.Log("sendlen:"+imgbyt.Length/1024);
        client.BeginSend( head , 0,  head.Length, 0, Sendcallback, client);
        client.BeginSend(imagebytes, 0, imagebytes.Length, 0, Sendcallback, client);
        client.BeginReceive(mess, 0, mess.Length, SocketFlags.None, ReceviceCallBack, client);
    }

    private void ReceviceCallBack(IAsyncResult ar)
    {
        try
        {
            client = ar.AsyncState as Socket;
            int count = client.EndReceive(ar);  //��ȡ�������ݵĳ���
            if (count == 0)
            {
                client.Close();
                return;
            }
            else ReadMessage(mess);
        }
        catch (Exception e)
        {
            Debug.Log(e.Message);
        }
    }

    private void ReadMessage(byte[] mess)
    {
        MemoryStream memoryStream = new(mess);
        XmlReader xmlreader = new XmlTextReader(memoryStream);
        XmlDocument xml = new();
        xml.Load(xmlreader);
        var k = xml.GetElementsByTagName("item");
        kf = new float[k.Count];
        for (int i = 0; i < k.Count; i++)
        {
            kf[i] = float.Parse(k[i].InnerText);
            Debug.Log(kf[i]);
        }
    }

    private void OnEnable() {
        cameraManager.frameReceived += OnCameraFrameReceived;
    }

    private void OnDisable() {
        cameraManager.frameReceived -= OnCameraFrameReceived;
    }

    unsafe void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!scanningEnabled)
        {
            return;
        }
        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            return;
        }

        var format = TextureFormat.RGBA32;

        if (m_Texture == null || m_Texture.width != image.width || m_Texture.height != image.height)
        {
            m_Texture = new Texture2D(image.width, image.height, format, false);
        }

        // ��ͼ��ת��Ϊ��ʽ����Y���Ϸ�תͼ������Ҳ���Եõ�һ���Ӿ��Σ������ǻ�õ�������ͼ��
        var conversionParams = new XRCpuImage.ConversionParams(image, format, XRCpuImage.Transformation.MirrorY);

        // Texture2D��������ֱ��д��ԭʼ�������ݣ������������ڲ������κθ��Ƶ�����½��о͵�ת����
        var rawTextureData = m_Texture.GetRawTextureData<byte>();
        try
        {
            image.Convert(conversionParams, new IntPtr(rawTextureData.GetUnsafePtr()), rawTextureData.Length);
        }
        finally
        {
            // ���Ǳ�����ʹ����XRCameraImage֮���������Ա���й©������Դ��
            image.Dispose();
        }

        // �����º����������Ӧ�õ����ǵ�������
        m_Texture.Apply();
        imagebytes = m_Texture.EncodeToJPG();
        if(lenoflastjpg!=imagebytes.Length)
        {
            lenoflastjpg = imagebytes.Length;
            head = Getimghead(imagebytes.Length); 
            ConnectAsync();
            ToggleScanning();
        }

    }

    private void SetPos(float[] xnl)
    {
        Vector3 p = new(xnl[0], xnl[1], xnl[2]);
        Quaternion q = new(xnl[3], xnl[4], xnl[5], xnl[6]);
        //��Ԫ��ת����ŷ����
        Vector3 v3 =q.eulerAngles;
        v3.z = 0;
        //ŷ����ת������Ԫ��    
        // Reset position and rotation of ARSession
        session.Reset();

        // Add offset for recentering
        sessionOrigin.transform.position = p;
        sessionOrigin.transform.rotation = Quaternion.Euler(v3);
    }

    public void ToggleScanning()
    {
        scanningEnabled = !scanningEnabled;
    }
}
