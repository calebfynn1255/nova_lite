#!/usr/bin/env bash
set -e

mkdir -p model

MODEL_PATH="model/model.gguf"
URL="https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q8_0.gguf"

if [ -f "$MODEL_PATH" ]; then
    echo "Model weights already present at $MODEL_PATH."
    exit 0
fi

echo "Downloading Llama-3.2-1B-Instruct GGUF model for NovaLite Corporate Workspace..."
curl -L -o "$MODEL_PATH" "$URL"
echo "Download complete: $MODEL_PATH"
