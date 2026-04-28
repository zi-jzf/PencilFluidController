using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Concurrent;

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

            //JsonUtilityを使って文字列stringからPencildataオブジェクトに変換
            JsonUtility.FromJsonOverwrite(jsonString, CurrentData);
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
