from fastapi import FastAPI
import torch
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor

app = FastAPI()

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