using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Concurrent;
using System;

//iPadで入力されたデータを保持するクラス
[System.Serializable]
public class PencilData
{
    public float x;
    public float y;
    public float pressure;
    public float tiltX;
    public float tiltY;
    public bool isPressed;
}

//元画像・マスク画像受信用
[System.Serializable]
public class ImagePairData
{
    public string type;
    public string baseImageData;
    public string maskImageData;
}

//クライアント(iPad)と接続された際の振る舞いを定義するクラス
public class ApplePencilBehavior : WebSocketBehavior
{
    //別スレッドから送られてくる受信データを安全に保持するスレッドセーフなキュー
    public static ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    protected override void OnMessage(MessageEventArgs e)
    {
        //iPadからデータを受信した際に呼ばれる(バックグランドスレッド)
        messageQueue.Enqueue(e.Data);
    }
}

public class PencilReceiver : MonoBehaviour
{
    [Header("Server Settings")]
    public int port = 8080; //ポート番号

    private WebSocketServer wss;

    //最新の受信データを保持する変数（public staticなので、他のスクリプトからもアクセス可能）
    public static PencilData CurrentData = new PencilData();

    //画像の変換が終わったことを他のスクリプトに知らせるためのイベント
    public static Action<Texture2D, Texture2D> OnImagesReceived;

    void Start()
    {
        //サーバーの立ち上げ
        wss = new WebSocketServer(port);

        //エンドポイントの設定（例: ws://PCのIPアドレス:8080/pencil で接続可能になる）
        wss.AddWebSocketService<ApplePencilBehavior>("/pencil");
        wss.Start();

        Debug.Log($"[WebSocket] サーバーを起動しました。ポート：{port}");
    }

    void Update()
    {
        //キューに溜まった受信データをメインスレッドで取り出して処理する
        //※Unityのオブジェクト操作はメインスレッドで行う必要があるための設計です
        while (ApplePencilBehavior.messageQueue.TryDequeue(out string jsonString))
        {
            //受信できたか確認するためのデバックログ
            //Debug.Log($"[iPadからの受信データ]: {jsonString}");
            
            //届いたデータにmaskという文字が含まれているかどうか
            //:(コロン)の後のスペースの有無はブラウザや環境によるスペースの有無に対応している
            if(jsonString.Contains("\"type\":\"image_pair\"") || jsonString.Contains("\"type\": \"image_pair\""))
            {
                ProcessImageData(jsonString);
            }
            else
            {
                //JsonUtilityを使って文字列stringからPencildataオブジェクトに変換
                JsonUtility.FromJsonOverwrite(jsonString, CurrentData);
            }
            
        }
    }

    //文字列(Base64)からTexture2Dへの変換処理
    private void ProcessImageData(string jsonString)
    {
        try
        {
            // JSONからimageDataの文字列を取り出す
            ImagePairData data = JsonUtility.FromJson<ImagePairData>(jsonString);
            
            // "data:image/png;base64," のような余計なヘッダー部分を切り捨てる
            string base64Base = data.baseImageData.Split(',')[1];
            
            // 文字列をバイト配列(01のデータ)に変換
            byte[] baseBytes = Convert.FromBase64String(base64Base);

            // 空のテクスチャを作って、画像データを流し込む（サイズは自動で調整されます）
            Texture2D baseTexture = new Texture2D(2, 2);
            baseTexture.LoadImage(baseBytes);

            //マスク画像の変換
            string base64Mask = data.maskImageData.Split(',')[1];
            byte[] maskBytes = Convert.FromBase64String(base64Mask);
            Texture2D maskTexture = new Texture2D(2, 2);
            maskTexture.LoadImage(maskBytes);

            Debug.Log($"元画像とマスク画像を正常に受信・変換しました。(サイズ: {maskTexture.width}x{maskTexture.height})");

            // 画像を待っている他のスクリプト（シミュレーション側）にテクスチャを渡す
            OnImagesReceived?.Invoke(baseTexture, maskTexture);
        }
        catch (Exception e)
        {
            Debug.LogError("画像の変換に失敗しました: " + e.Message);
        }
    }

    void OnDestroy()
    {
        //修了時やリロード時に確実にサーバーを閉じる
        if(wss != null)
        {
            wss.Stop();
            wss = null;
        }
    }
}
