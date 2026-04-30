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

//マスク画像受信用
[System.Serializable]
public class MaskData
{
    public string type;
    public string imageData;
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
    public static Action<Texture2D> OnMaskReceived;

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
            if(jsonString.Contains("\"type\":\"mask\"") || jsonString.Contains("\"type\": \"mask\""))
            {
                ProcessMaskData(jsonString);
            }
            else
            {
                //JsonUtilityを使って文字列stringからPencildataオブジェクトに変換
                JsonUtility.FromJsonOverwrite(jsonString, CurrentData);
            }
            
        }
    }

    //文字列(Base64)からTexture2Dへの変換処理
    private void ProcessMaskData(string jsonString)
    {
        try
        {
            // JSONからimageDataの文字列を取り出す
            MaskData maskData = JsonUtility.FromJson<MaskData>(jsonString);
            
            // "data:image/png;base64," のような余計なヘッダー部分を切り捨てる
            string base64Data = maskData.imageData.Split(',')[1];
            
            // 文字列をバイト配列(01のデータ)に変換
            byte[] imageBytes = Convert.FromBase64String(base64Data);

            // 空のテクスチャを作って、画像データを流し込む（サイズは自動で調整されます）
            Texture2D maskTexture = new Texture2D(2, 2);
            maskTexture.LoadImage(imageBytes);

            Debug.Log($"AIマスク画像を正常に受信・変換しました。(サイズ: {maskTexture.width}x{maskTexture.height})");

            // 画像を待っている他のスクリプト（シミュレーション側）にテクスチャを渡す
            OnMaskReceived?.Invoke(maskTexture);
        }
        catch (Exception e)
        {
            Debug.LogError("マスク画像の変換に失敗しました: " + e.Message);
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
