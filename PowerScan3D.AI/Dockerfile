FROM python:3.10-slim

WORKDIR /app

# Install system dependencies for rasterio and opencv
RUN apt-get update && apt-get install -y \
    gdal-bin \
    libgdal-dev \
    libgl1 \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Set GDAL environment variables
ENV CPLUS_INCLUDE_PATH=/usr/include/gdal
ENV C_INCLUDE_PATH=/usr/include/gdal

# Install PyTorch CPU first
RUN pip install torch torchvision --extra-index-url https://download.pytorch.org/whl/cpu

# Install the rest of requirements
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Pre-download DeepForest weights during build


COPY . /app

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]


