#!/bin/bash
INPUT_DIR='src/Renderer/Shaders/'
OUTPUT_DIR='compiled_shaders/'

# Check if directories exist
if [ ! -d "$INPUT_DIR" ]; then
    echo "ERROR: Input directory $INPUT_DIR does not exist"
    exit 1
fi

if [ ! -d "$OUTPUT_DIR" ]; then
    echo "ERROR: Output directory $OUTPUT_DIR does not exist"
    exit 1
fi

for FILE in "$OUTPUT_DIR"*; do
    if [ -f $FILE ]; then
        rm -f $FILE
    fi
done

for FILE in "$INPUT_DIR"*; do
    if [ -f "$FILE" ]; then
        FILENAME=$(basename "$FILE");
        glslc -o "$OUTPUT_DIR/$FILENAME.spv" "$FILE";
    fi
done

