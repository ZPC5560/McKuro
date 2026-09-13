using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Live2DCSharpSDK.OpenGL;

namespace Sparkle.Live2DView.Internal;

/// <summary>
/// 把 Live2DCSharpSDK 的 OpenGL 调用转发给 Avalonia。
/// 这里只做 API 适配，不处理模型业务。
/// </summary>
internal sealed class AvaloniaOpenGlApi(OpenGlControlBase control, GlInterface gl) : OpenGLApi
{
    public override bool IsES2 => true;
    public override bool AlwaysClear => true;
    public override bool IsPhoneES2 => false;

    private delegate void Int2(int a, int b);
    private delegate void Int4(int a, int b, int c, int d);
    private delegate void Float1(float a);
    private delegate void Bool4(bool a, bool b, bool c, bool d);
    private delegate void Int1(int a);
    private unsafe delegate void GetBool(int name, bool* value);
    private unsafe delegate void GetInt(int name, int* value);
    private delegate void GetVertexInt(int index, int name, out int value);
    private delegate bool IsEnabledDelegate(int name);
    private delegate void Int2Float(int a, int b, float value);
    private delegate void IntFloat4(int location, float a, float b, float c, float d);

    private readonly Int2 _blendFunc = Get<Int2>(gl, "glBlendFunc");
    private readonly Int4 _blendFuncSeparate = Get<Int4>(gl, "glBlendFuncSeparate");
    private readonly Float1 _clearDepth = Get<Float1>(gl, "glClearDepthf");
    private readonly Bool4 _colorMask = Get<Bool4>(gl, "glColorMask");
    private readonly Int2 _detachShader = Get<Int2>(gl, "glDetachShader");
    private readonly Int1 _disable = Get<Int1>(gl, "glDisable");
    private readonly Int1 _disableVertexArray = Get<Int1>(gl, "glDisableVertexAttribArray");
    private readonly Int1 _frontFace = Get<Int1>(gl, "glFrontFace");
    private readonly Int1 _generateMipmap = Get<Int1>(gl, "glGenerateMipmap");
    private readonly GetBool _getBoolean = Get<GetBool>(gl, "glGetBooleanv");
    private readonly GetInt _getInteger = Get<GetInt>(gl, "glGetIntegerv");
    private readonly GetVertexInt _getVertexInteger = Get<GetVertexInt>(gl, "glGetVertexAttribiv");
    private readonly IsEnabledDelegate _isEnabled = Get<IsEnabledDelegate>(gl, "glIsEnabled");
    private readonly Int2Float _texParameterFloat = Get<Int2Float>(gl, "glTexParameterf");
    private readonly Int2 _uniform1 = Get<Int2>(gl, "glUniform1i");
    private readonly IntFloat4 _uniform4 = Get<IntFloat4>(gl, "glUniform4f");
    private readonly Int1 _validateProgram = Get<Int1>(gl, "glValidateProgram");

    private static T Get<T>(GlInterface gl, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(gl.GetProcAddress(name));

    public override void GetWindowSize(out int width, out int height)
    {
        double scale = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
        width = Math.Max(1, (int)(control.Bounds.Width * scale));
        height = Math.Max(1, (int)(control.Bounds.Height * scale));
    }

    public override void ActiveTexture(int value) => gl.ActiveTexture(value);
    public override void AttachShader(int program, int shader) => gl.AttachShader(program, shader);
    public override void BindBuffer(int target, int buffer) => gl.BindBuffer(target, buffer);
    public override void BindFramebuffer(int target, int framebuffer) => gl.BindFramebuffer(target, framebuffer);
    public override void BindTexture(int target, int texture) => gl.BindTexture(target, texture);
    public override void BindVertexArrayOES(int array) => gl.BindVertexArray(array);
    public override void BlendFunc(int source, int destination) => _blendFunc(source, destination);
    public override void BlendFuncSeparate(int a, int b, int c, int d) => _blendFuncSeparate(a, b, c, d);
    public override void Clear(int mask) => gl.Clear(mask);
    public override void ClearColor(float r, float g, float b, float a) => gl.ClearColor(r, g, b, a);
    public override void ClearDepthf(float depth) => _clearDepth(depth);
    public override void ColorMask(bool r, bool g, bool b, bool a) => _colorMask(r, g, b, a);
    public override void CompileShader(int shader) => gl.CompileShader(shader);
    public override int CreateProgram() => gl.CreateProgram();
    public override int CreateShader(int type) => gl.CreateShader(type);
    public override void DeleteFramebuffer(int framebuffer) => gl.DeleteFramebuffer(framebuffer);
    public override void DeleteProgram(int program) => gl.DeleteProgram(program);
    public override void DeleteShader(int shader) => gl.DeleteShader(shader);
    public override void DeleteTexture(int texture) => gl.DeleteTexture(texture);
    public override void DetachShader(int program, int shader) => _detachShader(program, shader);
    public override void Disable(int value) => _disable(value);
    public override void DisableVertexAttribArray(int index) => _disableVertexArray(index);
    public override void DrawElements(int mode, int count, int type, nint indices) => gl.DrawElements(mode, count, type, indices);
    public override void Enable(int value) => gl.Enable(value);
    public override void EnableVertexAttribArray(int index) => gl.EnableVertexAttribArray(index);
    public override void FramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level) =>
        gl.FramebufferTexture2D(target, attachment, textureTarget, texture, level);
    public override void FrontFace(int mode) => _frontFace(mode);
    public override void GenerateMipmap(int target) => _generateMipmap(target);
    public override int GenFramebuffer() => gl.GenFramebuffer();
    public override int GenTexture() => gl.GenTexture();
    public override int GetAttribLocation(int program, string name) => gl.GetAttribLocationString(program, name);

    public override unsafe void GetBooleanv(int name, bool[] values)
    {
        fixed (bool* pointer = values) _getBoolean(name, pointer);
    }

    public override int GetError() => gl.GetError();
    public override void GetIntegerv(int name, out int value) => gl.GetIntegerv(name, out value);

    public override unsafe void GetIntegerv(int name, int[] values)
    {
        fixed (int* pointer = values) _getInteger(name, pointer);
    }

    public override unsafe void GetProgramInfoLog(int program, out string log)
    {
        int length;
        gl.GetProgramiv(program, GL_INFO_LOG_LENGTH, &length);
        byte[] bytes = new byte[Math.Max(1, length)];
        fixed (byte* pointer = bytes)
        {
            gl.GetProgramInfoLog(program, bytes.Length, out int written, pointer);
            log = Encoding.UTF8.GetString(bytes, 0, written);
        }
    }

    public override unsafe void GetProgramiv(int program, int name, int* value) => gl.GetProgramiv(program, name, value);

    public override unsafe void GetShaderInfoLog(int shader, out string log)
    {
        int length;
        gl.GetShaderiv(shader, GL_INFO_LOG_LENGTH, &length);
        byte[] bytes = new byte[Math.Max(1, length)];
        fixed (byte* pointer = bytes)
        {
            gl.GetShaderInfoLog(shader, bytes.Length, out int written, pointer);
            log = Encoding.UTF8.GetString(bytes, 0, written);
        }
    }

    public override unsafe void GetShaderiv(int shader, int name, int* value) => gl.GetShaderiv(shader, name, value);
    public override int GetUniformLocation(int program, string name) => gl.GetUniformLocationString(program, name);
    public override void GetVertexAttribiv(int index, int name, out int value) => _getVertexInteger(index, name, out value);
    public override bool IsEnabled(int value) => _isEnabled(value);
    public override void LinkProgram(int program) => gl.LinkProgram(program);
    public override void ShaderSource(int shader, string source) => gl.ShaderSourceString(shader, source);
    public override void TexImage2D(int target, int level, int internalFormat, int width, int height, int border, int format, int type, nint data) =>
        gl.TexImage2D(target, level, internalFormat, width, height, border, format, type, data);
    public override void TexParameterf(int target, int name, float value) => _texParameterFloat(target, name, value);
    public override void TexParameteri(int target, int name, int value) => gl.TexParameteri(target, name, value);
    public override void Uniform1i(int location, int value) => _uniform1(location, value);
    public override void Uniform4f(int location, float a, float b, float c, float d) => _uniform4(location, a, b, c, d);

    public override unsafe void UniformMatrix4fv(int location, int count, bool transpose, float[] values)
    {
        fixed (float* pointer = values) gl.UniformMatrix4fv(location, count, transpose, pointer);
    }

    public override void UseProgram(int program) => gl.UseProgram(program);
    public override void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, nint pointer) =>
        gl.VertexAttribPointer(index, size, type, normalized ? 1 : 0, stride, pointer);
    public override void Viewport(int x, int y, int width, int height) => gl.Viewport(x, y, width, height);
    public override int GenBuffer() => gl.GenBuffer();
    public override void BufferData(int target, int size, nint data, int usage) => gl.BufferData(target, size, data, usage);
    public override int GenVertexArray() => gl.GenVertexArray();
    public override void BindVertexArray(int array) => gl.BindVertexArray(array);
    public override void ValidateProgram(int program) => _validateProgram(program);
}
