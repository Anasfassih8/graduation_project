import cv2
import numpy as np
from ultralytics import YOLO
import time
import json
import os
from datetime import datetime
import pika

# ==========================================================
# 1. CONFIGURATION & STREET LENGTH
# ==========================================================
VIDEO_PATH = r"C:\Users\ANAS EL FASEH\OneDrive\Pictures\mahmodya traffic.mp4"

OUTPUT_DIR = r"G:\outputs"
os.makedirs(OUTPUT_DIR, exist_ok=True)

OUTPUT_VIDEO_PATH = os.path.join(OUTPUT_DIR, "processed_output.mp4")
JSON_OUTPUT_PATH = os.path.join(OUTPUT_DIR, "vehicle_data.jsonl")

MODEL_PATH = r"D:\Grad_Project\best.pt"

# ACTUAL STREET MEASUREMENTS FOR THE ROI (In Meters)
REAL_WORLD_WIDTH = 10
REAL_WORLD_HEIGHT = 50

print("--- Loading AI Model... ---")
model = YOLO(MODEL_PATH)

# ==========================================================
# 2. ROI SELECTION (MAX 4 POINTS)
# ==========================================================
cap = cv2.VideoCapture(VIDEO_PATH)
ret, first_frame = cap.read()

if not ret:
    print("Error: Video not found.")
    exit()

roi_points = []
point_labels = ["Top-Left", "Top-Right", "Bottom-Right", "Bottom-Left"]

def mouse_callback(event, x, y, flags, param):

    if event == cv2.EVENT_LBUTTONDOWN and len(roi_points) < 4:
        roi_points.append((x, y))

    elif event == cv2.EVENT_RBUTTONDOWN and roi_points:
        roi_points.pop()

cv2.namedWindow("ROI Selection")
cv2.setMouseCallback("ROI Selection", mouse_callback)

while True:

    temp_img = first_frame.copy()

    for i, pt in enumerate(roi_points):

        cv2.circle(temp_img, pt, 5, (0, 0, 255), -1)

        cv2.putText(
            temp_img,
            point_labels[i],
            (pt[0] + 10, pt[1]),
            0,
            0.5,
            (0, 255, 0),
            1
        )

    if len(roi_points) > 1:

        cv2.polylines(
            temp_img,
            [np.array(roi_points)],
            len(roi_points) == 4,
            (0, 255, 0),
            2
        )

    cv2.imshow("ROI Selection", temp_img)

    key = cv2.waitKey(10) & 0xFF

    if key == ord('c') and len(roi_points) == 4:
        break

    elif key == 27:
        cap.release()
        cv2.destroyAllWindows()
        exit()

cv2.destroyAllWindows()
cv2.waitKey(100)

# ==========================================================
# 3. PERSPECTIVE TRANSFORM
# ==========================================================
h_frame, w_frame = first_frame.shape[:2]

src_pts = np.float32(roi_points)

dst_pts = np.float32([
    [0, 0],
    [REAL_WORLD_WIDTH * 20, 0],
    [REAL_WORLD_WIDTH * 20, REAL_WORLD_HEIGHT * 20],
    [0, REAL_WORLD_HEIGHT * 20]
])

M = cv2.getPerspectiveTransform(src_pts, dst_pts)

def get_real_coords(px, py):

    point = np.array([[[px, py]]], dtype=np.float32)

    transformed = cv2.perspectiveTransform(point, M)

    return transformed[0][0] / 20.0

# ROI mask
mask = np.zeros((h_frame, w_frame), dtype=np.uint8)
cv2.fillPoly(mask, [np.array(roi_points)], 255)

# ==========================================================
# 4. VIDEO OUTPUT
# ==========================================================
fps = cap.get(cv2.CAP_PROP_FPS)

out = cv2.VideoWriter(
    OUTPUT_VIDEO_PATH,
    cv2.VideoWriter_fourcc(*'mp4v'),
    fps,
    (w_frame, h_frame)
)

# ==========================================================
# 5. TRACKING VARIABLES
# ==========================================================
prev_pos_meters = {}
car_speeds_kmh = {}

# Active cars inside ROI
current_cars_in_roi = set()

# ==========================================================
# 6. JSON FILE
# ==========================================================
json_file = open(JSON_OUTPUT_PATH, "w")

# ==========================================================
# 7. RABBITMQ SETUP
# ==========================================================
connection = pika.BlockingConnection(
    pika.ConnectionParameters(host='localhost')
)

channel = connection.channel()

channel.queue_declare(
    queue='vehicle_data_v2',
    durable=True
)

# ==========================================================
# 8. RATE LIMITING
# ==========================================================
last_sent_speed = {}
last_sent_time = {}

SEND_INTERVAL = 2.0
SPEED_THRESHOLD = 2.0

print("Processing video...")

# ==========================================================
# 9. MAIN LOOP
# ==========================================================
while True:

    ret, frame = cap.read()

    if not ret:
        break

    # Apply ROI mask
    roi_region = cv2.bitwise_and(frame, frame, mask=mask)

    # YOLO Tracking
    results = model.track(
        roi_region,
        persist=True,
        tracker="bytetrack.yaml",
        conf=0.25,
        verbose=False
    )

    display_frame = frame.copy()

    # Reset current frame set
    current_cars_in_roi.clear()

    if results[0].boxes.id is not None:

        boxes = results[0].boxes.xyxy.cpu().numpy()
        ids = results[0].boxes.id.cpu().numpy().astype(int)

        for box, track_id in zip(boxes, ids):

            x1, y1, x2, y2 = map(int, box)

            # Bottom-center point
            cx = (x1 + x2) / 2
            cy = y2

            # Check if inside ROI
            inside = cv2.pointPolygonTest(
                np.array(roi_points, dtype=np.int32),
                (int(cx), int(cy)),
                False
            )

            if inside >= 0:
                current_cars_in_roi.add(track_id)

            # ==================================================
            # REAL WORLD COORDS
            # ==================================================
            curr_m_x, curr_m_y = get_real_coords(cx, cy)

            # ==================================================
            # SPEED CALCULATION
            # ==================================================
            if track_id in prev_pos_meters:

                prev_m_x, prev_m_y = prev_pos_meters[track_id]

                distance_meters = np.sqrt(
                    (curr_m_x - prev_m_x) ** 2 +
                    (curr_m_y - prev_m_y) ** 2
                )

                speed_raw = (distance_meters * fps) * 3.6

                # Speed smoothing
                if track_id in car_speeds_kmh:

                    car_speeds_kmh[track_id] = (
                        car_speeds_kmh[track_id] * 0.8
                        + speed_raw * 0.2
                    )

                else:
                    car_speeds_kmh[track_id] = speed_raw

            # Update previous position
            prev_pos_meters[track_id] = (curr_m_x, curr_m_y)

            # ==================================================
            # VISUALS + DATA SENDING
            # ==================================================
            if track_id in car_speeds_kmh:

                speed_val = car_speeds_kmh[track_id]

                current_time = time.time()

                should_send = False

                if track_id not in last_sent_time:

                    should_send = True

                elif current_time - last_sent_time[track_id] >= SEND_INTERVAL:

                    if abs(
                        speed_val -
                        last_sent_speed.get(track_id, 0)
                    ) >= SPEED_THRESHOLD:

                        should_send = True

                # ==================================================
                # EVENT OBJECT
                # ==================================================
                if should_send:

                    event = {
                        "road_id": "road_1",
                        "vehicle_id": int(track_id),
                        "timestamp": datetime.utcnow().isoformat(),
                        "speed_kmh": round(float(speed_val), 2),
                        "position": {
                            "x": round(float(curr_m_x), 2),
                            "y": round(float(curr_m_y), 2),
                        },
                        "cars_in_roi": len(current_cars_in_roi)
                    }

                    # ==========================
                    # JSON LOGGING
                    # ==========================
                    json_file.write(json.dumps(event) + "\n")

                    # ==========================
                    # RABBITMQ PUBLISH
                    # ==========================
                    channel.basic_publish(
                        exchange='',
                        routing_key='vehicle_data_v2',
                        body=json.dumps(event),
                        properties=pika.BasicProperties(
                            delivery_mode=2
                        )
                    )

                    print("Sent:", event)

                    last_sent_time[track_id] = current_time
                    last_sent_speed[track_id] = speed_val

                # ==================================================
                # DRAWING
                # ==================================================
                cv2.rectangle(
                    display_frame,
                    (x1, y1),
                    (x2, y2),
                    (0, 255, 0),
                    2
                )

                cv2.putText(
                    display_frame,
                    f"ID:{track_id} {speed_val:.1f} km/h",
                    (x1, y1 - 10),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (0, 255, 0),
                    2
                )

    # ==========================================================
    # DRAW ROI
    # ==========================================================
    cv2.polylines(
        display_frame,
        [np.array(roi_points)],
        True,
        (255, 255, 0),
        2
    )

    # ==========================================================
    # DISPLAY CAR COUNT
    # ==========================================================
    cv2.putText(
        display_frame,
        f"Cars in ROI: {len(current_cars_in_roi)}",
        (30, 50),
        cv2.FONT_HERSHEY_SIMPLEX,
        1,
        (0, 0, 255),
        3
    )

    # ==========================================================
    # SHOW & SAVE
    # ==========================================================
    cv2.imshow(
        "Pysource Logic - Speed Detection",
        display_frame
    )

    out.write(display_frame)

    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

# ==========================================================
# CLEANUP
# ==========================================================
cap.release()
out.release()

cv2.destroyAllWindows()

json_file.close()

connection.close()

print(f"Done! Saved video to: {OUTPUT_VIDEO_PATH}")
print(f"JSON log saved to: {JSON_OUTPUT_PATH}")