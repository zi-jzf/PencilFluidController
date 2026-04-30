from fastapi import FastAPI, File, UploadFile, Form, Response
from fastapi.middleware.cors import CORSMiddleware
import torch
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor
import numpy as np
import cv2

app = FastAPI()

# CORS設定(iPadのブラウザからのアクセスを許可する)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], # 開発中なのでとりあえず全てのアクセスを許可
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# AIモデルの準備
# RTX4060(CUDA)を使用。利用不可ならCPUにフォールバック
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
print(f"使用デバイス：{device}")

# bfloat16(半精度)を使ってVRAM消費を抑えつつ計算を高速化する設定
if device.type == "cuda":
    torch.autocast("cuda", dtype=torch.bfloat16).__enter__()
    #Ampere世代(RTX30/40系)の強力な機能TensorFloat32を有効化
    if torch.cuda.get_device_properties(0).major >= 8:
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True

# SAM2.1Tinyモデルの設定ファイルと重みファイルのパス
model_cfg = "configs/sam2.1/sam2.1_hiera_t.yaml"
sam2_checkpoint = "checkpoints/sam2.1_hiera_tiny.pt"

print("SAM2モデルを読み込んでいます...")
try:
    #モデルのビルドと推論器の作成
    sam2_model = build_sam2(model_cfg, sam2_checkpoint, device=device)
    predictor = SAM2ImagePredictor(sam2_model)
    print("モデルの読み込み成功。VRAM準備OK")
except Exception as e:
    print(f"モデルの読み込み失敗: {e}")

# APIエンドポイント
@app.get("/")
def read_root():
    return {"message": "SAM2 Image Segmentation Server is running."}

# 画像セグメンテーションの受け口
@app.post("/segment")
async def segment_image(
    file: UploadFile = File(...),
    x: float = Form(0.5), # タップしたX座標(0.0～1.0)
    y: float = Form(0.5)  # タップしたY座標(0.0～1.0)
):
    try:
        # 1.送られてきたバイナリデータをOpenCVの画像(配列)に変換
        contents = await file.read()
        nparr = np.frombuffer(contents, np.uint8)
        img_bgr = cv2.imdecode(nparr, cv2.IMREAD_COLOR)

        # OpenCVはBGR形式なのでAIが認識できるRGB形式に変換
        img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)

        # 2.SAM2に画像をセット(ここで画像の特徴を抽出)
        predictor.set_image(img_rgb)

        # 3.座標の変換(0.0~1.0の割合を実際のピクセル座標に直す)
        height, width, _ = img_rgb.shape
        pixel_x = int(x * width)
        pixel_y = int(y * height)

        input_point = np.array([[pixel_x, pixel_y]])
        input_label = np.array([1]) #1=切り抜きたい主役

        # 4.推論実行
        masks, scores, logits = predictor.predict(
            point_coords=input_point,
            point_labels=input_label,
            multimask_output=False #複数の候補ではなく一番自信のある1枚だけ返す
        )

        # 5.結果をUnityで使える白黒のPNG画像に変換
        mask = masks[0] #(H,W)の2次元配列
        mask_img = (mask * 255).astype(np.uint8) #True(主役)を白(255)、False(背景)を黒(0)に

        #PNGにエンコードして返す
        _, encoded_img = cv2.imencode('.png', mask_img)
        return Response(content=encoded_img.tobytes(), media_type="image/png")
    
    except Exception as e:
        return Response(content=f"Error: {str(e)}", status_code=500)
    finally:
        # 次の処理のためにVRAMを解放
        if torch.cuda.is_available():
            torch.cuda.empty_cache()