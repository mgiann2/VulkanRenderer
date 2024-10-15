rm -f shaders/tmp_vert.spv
rm -f shaders/tmp_frag.spv

glslc tmp_shader.vert -o shaders/tmp_vert.spv
glslc tmp_shader.frag -o shaders/tmp_frag.spv
