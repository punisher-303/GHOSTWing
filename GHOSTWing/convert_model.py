import os
import sys

try:
    from ultralytics import YOLO
except ImportError:
    print("Error: 'ultralytics' package not found.")
    print("Please run: pip install ultralytics")
    sys.exit(1)

def convert_model(input_path, output_name="GHOST_Vision.onnx"):
    if not os.path.exists(input_path):
        print(f"Error: Could not find model at {input_path}")
        return

    print(f"Loading model: {input_path}")
    model = YOLO(input_path)

    print("Exporting to GHOST format (this may take a minute)...")
    # Exporting at 320x320 for maximum speed in GHOSTWing
    model.export(format="onnx", imgsz=320, simplify=True)
    
    # The export creates a file in the same folder as input
    input_dir = os.path.dirname(input_path)
    base_name = os.path.splitext(os.path.basename(input_path))[0]
    exported_path = os.path.join(input_dir, f"{base_name}.onnx")
    
    if os.path.exists(exported_path):
        target_path = os.path.join(os.getcwd(), "GHOST_Intelligence", output_name)
        os.makedirs(os.path.dirname(target_path), exist_ok=True)
        
        # Move and rename to GHOSTWing models folder
        import shutil
        shutil.move(exported_path, target_path)
        print(f"Success! Model saved to: {target_path}")
    else:
        print("Error: Export failed.")

if __name__ == "__main__":
    # Default path to the sunone model
    default_path = r"E:\ANANDPERSIONAL\DISCORD\scripts\sunone_aimbot\models\sunxds_0.5.6.pt"
    
    path = input(f"Enter path to your .pt model (Default: {default_path}): ") or default_path
    convert_model(path)
