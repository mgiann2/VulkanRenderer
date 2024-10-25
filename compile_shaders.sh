rm -f shaders/gpass.vert.spv
rm -f shaders/gpass.frag.spv

rm -f shaders/composition.vert.spv
rm -f shaders/composition.frag.spv

glslc -o shaders/gpass.vert.spv gpass.vert
glslc -o shaders/gpass.frag.spv gpass.frag

glslc -o shaders/composition.vert.spv composition.vert
glslc -o shaders/composition.frag.spv composition.frag
