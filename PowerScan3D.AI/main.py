import os
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any
import rasterio
from deepforest import main as df_main
import numpy as np

app = FastAPI(title="PowerScan3D AI Engine")

import torch
model = df_main.deepforest()
model.model.load_state_dict(torch.load('/app/NEON.pt', map_location=model.device))

class Coordinate(BaseModel):
    Latitude: float
    Longitude: float
    Altitude: float = 0.0

class AnalyzeRequest(BaseModel):
    orthophoto_path: str
    dsm_path: str = ""
    corridor_width_m: float = 20.0
    kmz_lines: List[List[Coordinate]] = []

@app.post("/analyze")
async def analyze_orthophoto(req: AnalyzeRequest):
    base_windows_path = r"C:\Users\Cristian\Downloads"
    req_path = req.orthophoto_path.replace("\\", "/")
    base_path = base_windows_path.replace("\\", "/")
    if req_path.lower().startswith(base_path.lower()):
        rel_path = req_path[len(base_path):].lstrip("/")
        img_path = os.path.join("/downloads", rel_path)
    else:
        img_path = req.orthophoto_path

    if not os.path.exists(img_path):
        raise HTTPException(status_code=404, detail=f"Orthophoto not found: {img_path}")

    try:
        from rasterio.warp import transform as transform_crs
        from rasterio.mask import mask
        from shapely.geometry import LineString
        import pyproj
        from shapely.ops import transform as shapely_transform
        from shapely.ops import unary_union

        with rasterio.open(img_path) as src:
            src_transform = src.transform
            is_geographic = src.crs and src.crs.is_geographic
            
            out_transform = src_transform
            
            # 1. CROP IF KMZ LINES EXIST
            if req.kmz_lines:
                lines_4326 = []
                for segment in req.kmz_lines:
                    if len(segment) > 1:
                        lines_4326.append(LineString([(coord.Longitude, coord.Latitude) for coord in segment]))
                
                if lines_4326:
                    project = pyproj.Transformer.from_crs("EPSG:4326", src.crs, always_xy=True).transform
                    projected_lines = [shapely_transform(project, line) for line in lines_4326]
                    
                    buffer_dist = req.corridor_width_m * 1.5
                    if is_geographic:
                        buffer_dist = buffer_dist / 111320.0
                        
                    polygons = [line.buffer(buffer_dist) for line in projected_lines]
                    mask_polygon = unary_union(polygons)
                    
                    out_image, out_transform = mask(src, [mask_polygon], crop=True)
                else:
                    out_image = src.read()
            else:
                out_image = src.read()
                
            # 2. FILTER TO 3 BANDS (RGB)
            if out_image.shape[0] >= 3:
                img_data = out_image[0:3, :, :]
            else:
                img_data = np.array([out_image[0], out_image[0], out_image[0]])
                
            img_data = np.moveaxis(img_data, 0, 2)
            
            # 3. RUN DEEPFOREST
            boxes = model.predict_tile(image=img_data, return_plot=False, patch_size=400, patch_overlap=0.1)
            
            trees = []
            if boxes is not None and not boxes.empty:
                for _, row in boxes.iterrows():
                    center_x_px = (row['xmin'] + row['xmax']) / 2.0
                    center_y_px = (row['ymin'] + row['ymax']) / 2.0
                    
                    x_coord, y_coord = out_transform * (center_x_px, center_y_px)
                    
                    if not is_geographic:
                        try:
                            converted = transform_crs(src.crs, 'EPSG:4326', [x_coord], [y_coord])
                            lon = converted[0][0]
                            lat = converted[1][0]
                        except:
                            lon, lat = x_coord, y_coord
                    else:
                        lon, lat = x_coord, y_coord
                    
                    pixel_size_x = out_transform[0]
                    width_px = row['xmax'] - row['xmin']
                    
                    if is_geographic:
                        diam_m = width_px * pixel_size_x * 111320.0
                    else:
                        diam_m = width_px * pixel_size_x
                    
                    trees.append({
                        "lat": float(lat),
                        "lon": float(lon),
                        "crownDiam": float(diam_m),
                        "confidence": float(row['score']),
                        "species": "Desconocida"
                    })
                        
        return {"status": "success", "trees": trees, "count": len(trees)}
    except Exception as e:
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=str(e))
