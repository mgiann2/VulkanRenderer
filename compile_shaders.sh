rm -f shaders/gpass.vert.spv
rm -f shaders/gpass.frag.spv

rm -f shaders/composition.vert.spv
rm -f shaders/composition.frag.spv

rm -f shaders/light.vert.spv
rm -f shaders/light.frag.spv

glslc -o shaders/gpass.vert.spv gpass.vert
glslc -o shaders/gpass.frag.spv gpass.frag

glslc -o shaders/composition.vert.spv composition.vert
glslc -o shaders/composition.frag.spv composition.frag

glslc -o shaders/light.vert.spv light.vert
glslc -o shaders/light.frag.spv light.frag
