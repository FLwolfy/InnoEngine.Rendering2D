$input v_texcoord0, v_color0, v_shape
#include <bgfx_shader.sh>
SAMPLER2D(s_spriteTexture, 0);

void main()
{
    vec2 shapePosition = v_texcoord0 * 2.0 - 1.0;
    float shapeAlpha = 1.0;
    if (v_shape > 1.5 && v_shape < 2.5)
    {
        float distanceToEdge = 1.0 - length(shapePosition);
        shapeAlpha = smoothstep(-fwidth(distanceToEdge), fwidth(distanceToEdge), distanceToEdge);
    }
    else if (v_shape > 2.5 && v_shape < 3.5)
    {
        vec2 trianglePoint = vec2(shapePosition.x, -shapePosition.y);
        float edge = max(
            trianglePoint.y - 1.0,
            max(-trianglePoint.y - 2.0 * trianglePoint.x - 1.0,
                -trianglePoint.y + 2.0 * trianglePoint.x - 1.0));
        shapeAlpha = 1.0 - smoothstep(-fwidth(edge), fwidth(edge), edge);
    }
    else if (v_shape > 3.5 && v_shape < 4.5)
    {
        vec2 capsulePoint = vec2(shapePosition.x, max(abs(shapePosition.y) - 0.5, 0.0));
        float distanceToEdge = 0.5 - length(capsulePoint);
        shapeAlpha = smoothstep(-fwidth(distanceToEdge), fwidth(distanceToEdge), distanceToEdge);
    }

    vec4 source = v_shape < 0.5 ? texture2D(s_spriteTexture, v_texcoord0) : vec4(1.0);
    vec4 color = source * v_color0;
    color.a *= shapeAlpha;
    if (color.a <= 0.001)
        discard;
    gl_FragColor = color;
}
