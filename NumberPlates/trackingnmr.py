import cv2
import numpy as np
import math
import time
import os
import subprocess
import threading
import json
from datetime import datetime, timezone
from ultralytics import YOLO
import easyocr
import pika

# =====================================================
# CONFIG
# =====================================================

MODEL_PATH     = r"D:\Grad_Project\license_plate_detector.pt"
VIDEO_PATH     = r"G:\Downloads\Video from Anas Fassih.mp4"
ROAD_ID        = "road_1"          # change per camera

METERS_PER_PIXEL  = 10 / 250
SPEED_THRESHOLD   = 10             # km/h

SAVE_DIR = "screenshots"
os.makedirs(SAVE_DIR, exist_ok=True)

COOLDOWN_SEC = 3
last_saved = {}

RABBITMQ_HOST      = "localhost"
VIOLATIONS_QUEUE   = "violations"

# =====================================================
# MODEL + OCR INIT
# =====================================================

model      = YOLO(MODEL_PATH)
ocr_reader = easyocr.Reader(['ar'])   # loaded once at startup

# =====================================================
# RABBITMQ PUBLISH
# =====================================================

def publish_violation(plate_text: str, speed: float, track_id: int):
    """Publish a violation message to RabbitMQ. Called from a background thread."""
    try:
        connection = pika.BlockingConnection(
            pika.ConnectionParameters(host=RABBITMQ_HOST)
        )
        channel = connection.channel()
        channel.queue_declare(queue=VIOLATIONS_QUEUE, durable=True)

        message = {
    "plate_number": str(plate_text),
    "speed_kmh": float(round(speed, 1)),
    "track_id": int(track_id),
    "road_id": str(ROAD_ID),
    "timestamp": datetime.now(timezone.utc).isoformat()
}

        channel.basic_publish(
            exchange='',
            routing_key=VIOLATIONS_QUEUE,
            body=json.dumps(message, ensure_ascii=False),
            properties=pika.BasicProperties(delivery_mode=2)   # persistent
        )
        connection.close()
        print(f"[QUEUE] Published → plate: '{plate_text}'  speed: {speed:.1f} km/h")

    except Exception as e:
        print(f"[QUEUE ERROR] {e}")

# =====================================================
# OCR + PUBLISH  (runs on a background thread)
# =====================================================

def ocr_and_publish(crop_img: np.ndarray, track_id: int, speed: float):
    """
    Reads the license plate text from the cropped image, then publishes
    to RabbitMQ.  Must receive a *copy* of the numpy array because the
    main thread will keep writing to the original frame.
    """
    try:
        results    = ocr_reader.readtext(crop_img)
        plate_text = " ".join([r[1] for r in results]).strip()

        if plate_text:
            print(f"[OCR] Track {track_id}: '{plate_text}'")
            publish_violation(plate_text, speed, track_id)
        else:
            print(f"[OCR] Track {track_id}: no text detected — skipping publish")

    except Exception as e:
        print(f"[OCR ERROR] Track {track_id}: {e}")

# =====================================================
# SCREENSHOT + SPAWN OCR THREAD
# =====================================================

def save_screenshot(frame: np.ndarray, x1: int, y1: int, x2: int, y2: int,
                    track_id: int, speed: float):
    timestamp = time.strftime("%Y-%m-%d_%H-%M-%S")
    crop      = frame[y1:y2, x1:x2]
    filename  = f"{SAVE_DIR}/ID_{track_id}_{speed:.1f}kmh_{timestamp}.jpg"

    cv2.imwrite(filename, crop)
    print(f"[VIOLATION] Screenshot saved: {filename}")

    # Spawn background thread with a copy of the crop
    thread = threading.Thread(
        target=ocr_and_publish,
        args=(crop.copy(), track_id, speed),
        daemon=True
    )
    thread.start()

# =====================================================
# ROI SELECTION
# =====================================================

roi_points = []

def mouse_callback(event, x, y, flags, param):
    global roi_points
    if event == cv2.EVENT_LBUTTONDOWN and len(roi_points) < 4:
        roi_points.append((x, y))
        print(f"Point {len(roi_points)}: {roi_points[-1]}")

cap = cv2.VideoCapture(VIDEO_PATH)
ret, first_frame = cap.read()
if not ret:
    print("Cannot read video")
    exit()

clone = first_frame.copy()
cv2.namedWindow("Select ROI")
cv2.setMouseCallback("Select ROI", mouse_callback)

while True:
    temp = clone.copy()
    for p in roi_points:
        cv2.circle(temp, p, 5, (0, 255, 0), -1)
    if len(roi_points) > 1:
        cv2.polylines(temp, [np.array(roi_points)], False, (0, 255, 0), 2)
    cv2.putText(temp, f"Select 4 points ({len(roi_points)}/4)",
                (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)
    cv2.imshow("Select ROI", temp)
    key = cv2.waitKey(1)
    if key == ord('r'):
        roi_points = []
    if key == 27:
        cap.release(); cv2.destroyAllWindows(); exit()
    if len(roi_points) == 4:
        break

cv2.destroyWindow("Select ROI")
ROI = np.array(roi_points, dtype=np.int32)

# =====================================================
# VIDEO SETUP
# =====================================================

cap.release()
cap = cv2.VideoCapture(VIDEO_PATH)

width  = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
fps    = cap.get(cv2.CAP_PROP_FPS) or 30

base         = os.path.splitext(os.path.basename(VIDEO_PATH))[0]
raw_output   = f"{base}_raw.mp4"
final_output = f"{base}_WHATSAPP.mp4"

fourcc = cv2.VideoWriter_fourcc(*'mp4v')
writer = cv2.VideoWriter(raw_output, fourcc, fps, (width, height))

track_history = {}
print("Processing video…")

# =====================================================
# MAIN LOOP
# =====================================================

while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        break

    current_time = time.time()

    results = model.track(
        frame,
        persist=True,
        tracker="bytetrack.yaml",
        verbose=False
    )

    cv2.polylines(frame, [ROI], True, (0, 255, 0), 2)

    if results[0].boxes is not None and results[0].boxes.id is not None:
        boxes = results[0].boxes.xyxy.cpu().numpy()
        confs = results[0].boxes.conf.cpu().numpy()
        ids   = results[0].boxes.id.cpu().numpy().astype(int)

        for box, conf, tid in zip(boxes, confs, ids):
            x1, y1, x2, y2 = map(int, box)
            cx = (x1 + x2) // 2
            cy = (y1 + y2) // 2

            if cv2.pointPolygonTest(ROI, (cx, cy), False) < 0:
                continue

            speed = 0.0
            if tid in track_history:
                px, py, pt = track_history[tid]
                dist  = math.sqrt((cx - px) ** 2 + (cy - py) ** 2)
                dt    = current_time - pt
                if dt > 0:
                    speed = (dist * METERS_PER_PIXEL / dt) * 3.6

            track_history[tid] = (cx, cy, current_time)

            if speed > SPEED_THRESHOLD:
                if current_time - last_saved.get(tid, 0) > COOLDOWN_SEC:
                    save_screenshot(frame, x1, y1, x2, y2, tid, speed)
                    last_saved[tid] = current_time

            cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 0, 255), 2)
            cv2.circle(frame, (cx, cy), 4, (255, 0, 0), -1)
            cv2.putText(frame,
                        f"ID:{tid}  {speed:.1f} km/h  {conf:.2f}",
                        (x1, y1 - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)

    writer.write(frame)
    cv2.imshow("System", frame)
    if cv2.waitKey(1) == 27:
        break

cap.release()
writer.release()
cv2.destroyAllWindows()
print("Raw video saved:", raw_output)

# =====================================================
# WHATSAPP COMPATIBILITY FIX
# =====================================================

print("Converting to WhatsApp format…")
FFMPEG_PATH = r"C:\Users\ANAS EL FASEH\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe"

subprocess.run([
    FFMPEG_PATH,
    "-y",
    "-i", raw_output,
    "-c:v", "libx264",
    "-profile:v", "baseline",
    "-level", "3.0",
    "-pix_fmt", "yuv420p",
    "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
    "-r", "30",
    final_output
], check=True)
print(f"\nDONE!\nFinal video : {final_output}\nScreenshots : {SAVE_DIR}")