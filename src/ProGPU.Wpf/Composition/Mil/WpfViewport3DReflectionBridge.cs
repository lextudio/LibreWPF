using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Windows;
using ProGPU.Wpf.Interop;
using ProGpuLinearGradientBrush = ProGPU.Vector.LinearGradientBrush;
using ProGpuRadialGradientBrush = ProGPU.Vector.RadialGradientBrush;
using ProGpuSolidColorBrush = ProGPU.Vector.SolidColorBrush;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public static class WpfViewport3DReflectionBridge
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float DefaultNearPlaneDistance = 0.125f;
    private const float DefaultFarPlaneDistance = 1000f;
    private const float DefaultPerspectiveFieldOfView = 45f;
    private const float DefaultOrthographicWidth = 2f;

    public static bool TryCreateReplayData(object viewportVisual, out WpfViewport3DReplayData replayData)
    {
        return TryCreateReplayData(viewportVisual, textureCache: null, out replayData);
    }

    internal static bool TryCreateReplayData(
        object viewportVisual,
        WpfViewport3DTextureCache? textureCache,
        out WpfViewport3DReplayData replayData)
    {
        ArgumentNullException.ThrowIfNull(viewportVisual);

        if (viewportVisual is IPortableViewport3DSceneSource portableSceneSource)
        {
            if (portableSceneSource.TryGetPortableViewport3DScene(out var scene))
            {
                return TryCreateReplayDataFromPortableScene(
                    viewportVisual,
                    scene,
                    textureCache,
                    out replayData);
            }

            replayData = default;
            return false;
        }

        if (!TypeNameEndsWith(viewportVisual, "Viewport3DVisual")
            || !TryReadViewportBounds(viewportVisual, out var viewport)
            || !TryGetPropertyValue(viewportVisual, "Camera", out var camera)
            || camera == null)
        {
            replayData = default;
            return false;
        }

        var viewportWidth = Math.Max(1f, (float)viewport.Width);
        var viewportHeight = Math.Max(1f, (float)viewport.Height);
        var aspectRatio = viewportWidth / viewportHeight;

        if (!TryCreateCameraMatrices(camera, aspectRatio, out var projection, out var view))
        {
            replayData = default;
            return false;
        }

        var payload = new global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload
        {
            ViewportSize = new Vector2(viewportWidth, viewportHeight)
        };

        if (textureCache != null)
        {
            var textures = textureCache.GetOrCreate(
                viewportVisual,
                (uint)Math.Ceiling(viewportWidth),
                (uint)Math.Ceiling(viewportHeight));
            payload.ColorTexture = textures.ColorTexture;
            payload.MsaaColorTexture = textures.MsaaColorTexture;
            payload.DepthTexture = textures.DepthTexture;
        }

        if (TryGetPropertyValue(viewportVisual, "Children", out var children) && children != null)
        {
            foreach (var child in EnumerateCollection(children))
            {
                if (child != null)
                {
                    CompileVisual3D(child, Matrix4x4.Identity, payload);
                }
            }
        }

        replayData = new WpfViewport3DReplayData(
            payload,
            projection,
            view,
            new global::ProGPU.Scene.Rect(
                (float)viewport.X,
                (float)viewport.Y,
                viewportWidth,
                viewportHeight));
        return payload.Meshes.Count > 0;
    }

    private static bool TryCreateReplayDataFromPortableScene(
        object viewportVisual,
        PortableViewport3DScene scene,
        WpfViewport3DTextureCache? textureCache,
        out WpfViewport3DReplayData replayData)
    {
        replayData = default;
        if (scene.Camera == null
            || scene.Viewport.IsEmpty
            || scene.Viewport.Width <= 0
            || scene.Viewport.Height <= 0)
        {
            return false;
        }

        var viewportWidth = Math.Max(1f, (float)scene.Viewport.Width);
        var viewportHeight = Math.Max(1f, (float)scene.Viewport.Height);
        var aspectRatio = viewportWidth / viewportHeight;
        if (!TryCreateCameraMatrices(scene.Camera, aspectRatio, out var projection, out var view))
        {
            return false;
        }

        var payload = new global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload
        {
            ViewportSize = new Vector2(viewportWidth, viewportHeight),
            LightDirection = ToVector3(scene.LightDirection),
            LightIntensity = (float)Math.Clamp(scene.LightIntensity, 0, double.MaxValue),
            AmbientColor = ToVector3(scene.AmbientColor),
            AmbientIntensity = (float)Math.Clamp(scene.AmbientIntensity, 0, double.MaxValue)
        };

        if (textureCache != null)
        {
            var textures = textureCache.GetOrCreate(
                viewportVisual,
                (uint)Math.Ceiling(viewportWidth),
                (uint)Math.Ceiling(viewportHeight));
            payload.ColorTexture = textures.ColorTexture;
            payload.MsaaColorTexture = textures.MsaaColorTexture;
            payload.DepthTexture = textures.DepthTexture;
        }

        foreach (var mesh in scene.Meshes)
        {
            if (mesh == null
                || mesh.Positions.Length == 0
                || mesh.Indices.Length == 0)
            {
                continue;
            }

            payload.Meshes.Add(new global::ProGPU.Scene.Extensions.MeshCompilationEntry
            {
                Geometry = mesh.Geometry,
                GeometryVersion = mesh.GeometryVersion,
                Positions = ToVector3Array(mesh.Positions),
                Normals = ToVector3Array(mesh.Normals),
                Indices = mesh.Indices,
                ModelTransform = ToMatrix4x4(mesh.ModelTransform),
                Color = ToVector4(mesh.DiffuseColor),
                SpecularColor = ToVector3(mesh.SpecularColor),
                Shininess = (float)Math.Clamp(mesh.Shininess, 1, 256),
                AmbientColor = ToVector3(mesh.AmbientColor),
                Opacity = (float)Math.Clamp(mesh.Opacity, 0, 1),
                IsBackFace = mesh.IsBackFace
            });
        }

        replayData = new WpfViewport3DReplayData(
            payload,
            projection,
            view,
            new global::ProGPU.Scene.Rect(
                (float)scene.Viewport.X,
                (float)scene.Viewport.Y,
                viewportWidth,
                viewportHeight));
        return payload.Meshes.Count > 0;
    }

    private static void CompileVisual3D(
        object visual,
        Matrix4x4 parentTransform,
        global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload payload)
    {
        var visualTransform = ReadTransform3DPropertyOrIdentity(visual, "Transform");
        var localTransform = visualTransform * parentTransform;

        if (TryGetPropertyValue(visual, "Content", out var content) && content != null)
        {
            CompileModel3D(content, localTransform, payload);
        }

        if (!TryGetPropertyValue(visual, "Children", out var children) || children == null)
        {
            return;
        }

        foreach (var child in EnumerateCollection(children))
        {
            if (child != null)
            {
                CompileVisual3D(child, localTransform, payload);
            }
        }
    }

    private static void CompileModel3D(
        object model,
        Matrix4x4 parentTransform,
        global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload payload)
    {
        var modelTransform = ReadTransform3DPropertyOrIdentity(model, "Transform") * parentTransform;

        if (TryApplyLight(model, payload))
        {
            return;
        }

        if (TypeNameEndsWith(model, "Model3DGroup")
            && TryGetPropertyValue(model, "Children", out var children)
            && children != null)
        {
            foreach (var child in EnumerateCollection(children))
            {
                if (child != null)
                {
                    CompileModel3D(child, modelTransform, payload);
                }
            }

            return;
        }

        if (!TypeNameEndsWith(model, "GeometryModel3D")
            || !TryGetPropertyValue(model, "Geometry", out var geometry)
            || geometry == null
            || !TypeNameEndsWith(geometry, "MeshGeometry3D"))
        {
            return;
        }

        if (!TryCreateMeshData(geometry, out var positions, out var normals, out var indices)
            || positions.Length == 0
            || indices.Length == 0)
        {
            return;
        }

        TryReadIntProperty(geometry, "Version", out var geometryVersion);

        TryGetPropertyValue(model, "Material", out var material);
        TryGetPropertyValue(model, "BackMaterial", out var backMaterial);

        if (material != null || backMaterial == null)
        {
            payload.Meshes.Add(CreateMeshEntry(
                geometry,
                geometryVersion,
                positions,
                normals,
                indices,
                modelTransform,
                ReadMaterial(material),
                isBackFace: false));
        }

        if (backMaterial != null)
        {
            payload.Meshes.Add(CreateMeshEntry(
                geometry,
                geometryVersion,
                positions,
                normals,
                indices,
                modelTransform,
                ReadMaterial(backMaterial),
                isBackFace: true));
        }
    }

    private static global::ProGPU.Scene.Extensions.MeshCompilationEntry CreateMeshEntry(
        object geometry,
        int geometryVersion,
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        Matrix4x4 modelTransform,
        MaterialDescriptor material,
        bool isBackFace)
    {
        return new global::ProGPU.Scene.Extensions.MeshCompilationEntry
        {
            Geometry = geometry,
            GeometryVersion = geometryVersion,
            Positions = positions,
            Normals = normals,
            Indices = indices,
            ModelTransform = modelTransform,
            Color = material.DiffuseColor,
            SpecularColor = material.SpecularColor,
            Shininess = material.Shininess,
            AmbientColor = material.AmbientColor,
            Opacity = material.Opacity,
            IsBackFace = isBackFace
        };
    }

    private static bool TryApplyLight(
        object model,
        global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload payload)
    {
        if (TypeNameEndsWith(model, "DirectionalLight")
            && TryGetPropertyValue(model, "Direction", out var directionValue)
            && directionValue != null
            && TryReadVector3(directionValue, out var direction))
        {
            payload.LightDirection = direction.LengthSquared() > 0.000001f
                ? Vector3.Normalize(direction)
                : payload.LightDirection;
            payload.LightIntensity = TryGetPropertyValue(model, "Color", out var colorValue)
                && colorValue != null
                && TryReadColorVector4(colorValue, out var color)
                    ? Math.Max(color.X, Math.Max(color.Y, color.Z))
                    : payload.LightIntensity;
            return true;
        }

        if (TypeNameEndsWith(model, "AmbientLight")
            && TryGetPropertyValue(model, "Color", out var ambientColorValue)
            && ambientColorValue != null
            && TryReadColorVector4(ambientColorValue, out var ambientColor))
        {
            payload.AmbientColor = new Vector3(ambientColor.X, ambientColor.Y, ambientColor.Z);
            payload.AmbientIntensity = ambientColor.W;
            return true;
        }

        return false;
    }

    private static MaterialDescriptor ReadMaterial(object? material)
    {
        var descriptor = MaterialDescriptor.Default;
        if (material == null)
        {
            return descriptor;
        }

        if (TypeNameEndsWith(material, "MaterialGroup")
            && TryGetPropertyValue(material, "Children", out var children)
            && children != null)
        {
            foreach (var child in EnumerateCollection(children))
            {
                if (child != null)
                {
                    descriptor = descriptor.Merge(ReadMaterial(child));
                }
            }

            return descriptor;
        }

        if (TypeNameEndsWith(material, "DiffuseMaterial"))
        {
            if (TryGetPropertyValue(material, "Brush", out var brush) && brush != null)
            {
                descriptor = descriptor with { DiffuseColor = ReadBrushColor(brush, descriptor.DiffuseColor) };
            }

            if (TryGetPropertyValue(material, "Color", out var diffuseColor)
                && diffuseColor != null
                && TryReadColorVector4(diffuseColor, out var materialColor))
            {
                descriptor = descriptor with { DiffuseColor = MultiplyColor(descriptor.DiffuseColor, materialColor) };
            }

            if (TryGetPropertyValue(material, "AmbientColor", out var ambientColor)
                && ambientColor != null
                && TryReadColorVector4(ambientColor, out var materialAmbient))
            {
                descriptor = descriptor with { AmbientColor = new Vector3(materialAmbient.X, materialAmbient.Y, materialAmbient.Z) };
            }
        }

        if (TypeNameEndsWith(material, "SpecularMaterial"))
        {
            if (TryGetPropertyValue(material, "Brush", out var brush) && brush != null)
            {
                var specular = ReadBrushColor(brush, new Vector4(descriptor.SpecularColor, 1f));
                descriptor = descriptor with { SpecularColor = new Vector3(specular.X, specular.Y, specular.Z) };
            }

            if (TryReadDoubleProperty(material, "SpecularPower", out var specularPower))
            {
                descriptor = descriptor with { Shininess = (float)Math.Clamp(specularPower, 1, 256) };
            }
        }

        if (TryReadDoubleProperty(material, "Opacity", out var opacity))
        {
            descriptor = descriptor with { Opacity = (float)Math.Clamp(opacity, 0, 1) };
        }

        return descriptor with
        {
            Opacity = descriptor.Opacity * Math.Clamp(descriptor.DiffuseColor.W, 0f, 1f),
            DiffuseColor = new Vector4(descriptor.DiffuseColor.X, descriptor.DiffuseColor.Y, descriptor.DiffuseColor.Z, 1f)
        };
    }

    private static Vector4 ReadBrushColor(object brush, Vector4 fallback)
    {
        var nativeBrush = WpfReflectionResourceResolver.AdaptBrush(brush)?.ToNative();
        if (nativeBrush == null)
        {
            return fallback;
        }

        var opacity = Math.Clamp(nativeBrush.Opacity, 0f, 1f);
        return nativeBrush switch
        {
            ProGpuSolidColorBrush solid => ApplyOpacity(solid.Color, opacity),
            ProGpuLinearGradientBrush { Stops.Length: > 0 } linear => ApplyOpacity(linear.Stops[0].Color, opacity),
            ProGpuRadialGradientBrush { Stops.Length: > 0 } radial => ApplyOpacity(radial.Stops[0].Color, opacity),
            _ => fallback
        };
    }

    private static Vector4 ApplyOpacity(Vector4 color, float opacity)
    {
        return new Vector4(color.X, color.Y, color.Z, color.W * opacity);
    }

    private static Vector4 MultiplyColor(Vector4 left, Vector4 right)
    {
        return new Vector4(
            left.X * right.X,
            left.Y * right.Y,
            left.Z * right.Z,
            left.W * right.W);
    }

    private static bool TryCreateMeshData(
        object geometry,
        out Vector3[] positions,
        out Vector3[] normals,
        out int[] indices)
    {
        positions = Array.Empty<Vector3>();
        normals = Array.Empty<Vector3>();
        indices = Array.Empty<int>();

        if (!TryGetPropertyValue(geometry, "Positions", out var positionsValue)
            || positionsValue == null
            || !TryReadVector3Collection(positionsValue, out positions)
            || positions.Length == 0)
        {
            return false;
        }

        if (TryGetPropertyValue(geometry, "TriangleIndices", out var indicesValue)
            && indicesValue != null
            && TryReadIntCollection(indicesValue, out var readIndices)
            && readIndices.Length > 0)
        {
            indices = readIndices;
        }
        else
        {
            indices = CreateSequentialTriangleIndices(positions.Length);
        }

        if (TryGetPropertyValue(geometry, "Normals", out var normalsValue)
            && normalsValue != null
            && TryReadVector3Collection(normalsValue, out var readNormals)
            && readNormals.Length == positions.Length)
        {
            normals = readNormals;
        }
        else
        {
            normals = ComputeNormals(positions, indices);
        }

        return indices.Length > 0;
    }

    private static int[] CreateSequentialTriangleIndices(int positionCount)
    {
        var triangleCount = positionCount / 3;
        if (triangleCount == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new int[triangleCount * 3];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        return indices;
    }

    private static Vector3[] ComputeNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
            {
                continue;
            }

            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var normal = Vector3.Cross(edge1, edge2);
            if (normal.LengthSquared() <= 0.000001f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > 0.000001f
                ? Vector3.Normalize(normals[i])
                : Vector3.UnitZ;
        }

        return normals;
    }

    private static bool TryCreateCameraMatrices(
        object camera,
        float aspectRatio,
        out Matrix4x4 projection,
        out Matrix4x4 view)
    {
        projection = Matrix4x4.Identity;
        view = Matrix4x4.Identity;

        if (!TryGetPropertyValue(camera, "Position", out var positionValue)
            || positionValue == null
            || !TryReadVector3(positionValue, out var position)
            || !TryGetPropertyValue(camera, "LookDirection", out var lookDirectionValue)
            || lookDirectionValue == null
            || !TryReadVector3(lookDirectionValue, out var lookDirection)
            || !TryGetPropertyValue(camera, "UpDirection", out var upDirectionValue)
            || upDirectionValue == null
            || !TryReadVector3(upDirectionValue, out var upDirection))
        {
            return false;
        }

        if (lookDirection.LengthSquared() <= 0.000001f || upDirection.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        if (TryGetPropertyValue(camera, "Transform", out var transform)
            && transform != null
            && TryReadTransform3D(transform, out var cameraTransform))
        {
            position = Vector3.Transform(position, cameraTransform);
            lookDirection = Vector3.TransformNormal(lookDirection, cameraTransform);
            upDirection = Vector3.TransformNormal(upDirection, cameraTransform);
        }

        view = Matrix4x4.CreateLookAt(position, position + lookDirection, upDirection);

        var nearPlane = ReadPositiveFloatProperty(camera, "NearPlaneDistance", DefaultNearPlaneDistance);
        var farPlane = ReadPositiveFloatProperty(camera, "FarPlaneDistance", DefaultFarPlaneDistance);
        if (farPlane <= nearPlane)
        {
            farPlane = nearPlane + 1f;
        }

        if (TypeNameEndsWith(camera, "OrthographicCamera"))
        {
            var width = ReadPositiveFloatProperty(camera, "Width", DefaultOrthographicWidth);
            var height = width / Math.Max(0.0001f, aspectRatio);
            projection = Matrix4x4.CreateOrthographic(width, height, nearPlane, farPlane);
            return true;
        }

        var horizontalFovDegrees = ReadPositiveFloatProperty(camera, "FieldOfView", DefaultPerspectiveFieldOfView);
        horizontalFovDegrees = Math.Clamp(horizontalFovDegrees, 1f, 179f);
        var horizontalFovRadians = horizontalFovDegrees * MathF.PI / 180f;
        var verticalFovRadians = 2f * MathF.Atan(MathF.Tan(horizontalFovRadians / 2f) / Math.Max(0.0001f, aspectRatio));
        projection = Matrix4x4.CreatePerspectiveFieldOfView(verticalFovRadians, aspectRatio, nearPlane, farPlane);
        return true;
    }

    private static bool TryCreateCameraMatrices(
        PortableViewport3DCamera camera,
        float aspectRatio,
        out Matrix4x4 projection,
        out Matrix4x4 view)
    {
        projection = Matrix4x4.Identity;
        view = Matrix4x4.Identity;

        var position = ToVector3(camera.Position);
        var lookDirection = ToVector3(camera.LookDirection);
        var upDirection = ToVector3(camera.UpDirection);
        if (lookDirection.LengthSquared() <= 0.000001f || upDirection.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        if (camera.HasTransform)
        {
            var transform = ToMatrix4x4(camera.Transform);
            position = Vector3.Transform(position, transform);
            lookDirection = Vector3.TransformNormal(lookDirection, transform);
            upDirection = Vector3.TransformNormal(upDirection, transform);
        }

        view = Matrix4x4.CreateLookAt(position, position + lookDirection, upDirection);

        var nearPlane = camera.NearPlaneDistance > 0
            ? (float)camera.NearPlaneDistance
            : DefaultNearPlaneDistance;
        var farPlane = camera.FarPlaneDistance > nearPlane
            ? (float)camera.FarPlaneDistance
            : nearPlane + 1f;

        if (camera.Kind == PortableViewport3DCameraKind.Orthographic)
        {
            var width = camera.Width > 0
                ? (float)camera.Width
                : DefaultOrthographicWidth;
            var height = width / Math.Max(0.0001f, aspectRatio);
            projection = Matrix4x4.CreateOrthographic(width, height, nearPlane, farPlane);
            return true;
        }

        var horizontalFovDegrees = camera.FieldOfView > 0
            ? (float)camera.FieldOfView
            : DefaultPerspectiveFieldOfView;
        horizontalFovDegrees = Math.Clamp(horizontalFovDegrees, 1f, 179f);
        var horizontalFovRadians = horizontalFovDegrees * MathF.PI / 180f;
        var verticalFovRadians = 2f * MathF.Atan(MathF.Tan(horizontalFovRadians / 2f) / Math.Max(0.0001f, aspectRatio));
        projection = Matrix4x4.CreatePerspectiveFieldOfView(verticalFovRadians, aspectRatio, nearPlane, farPlane);
        return true;
    }

    private static Vector3 ToVector3(PortableVector3 value)
    {
        return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static Vector3 ToVector3(PortableColor4 value)
    {
        return new Vector3((float)value.R, (float)value.G, (float)value.B);
    }

    private static Vector4 ToVector4(PortableColor4 value)
    {
        return new Vector4((float)value.R, (float)value.G, (float)value.B, (float)value.A);
    }

    private static Vector3[] ToVector3Array(PortableVector3[] values)
    {
        if (values.Length == 0)
        {
            return Array.Empty<Vector3>();
        }

        var result = new Vector3[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = ToVector3(values[i]);
        }

        return result;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix4x4 value)
    {
        return new Matrix4x4(
            (float)value.M11, (float)value.M12, (float)value.M13, (float)value.M14,
            (float)value.M21, (float)value.M22, (float)value.M23, (float)value.M24,
            (float)value.M31, (float)value.M32, (float)value.M33, (float)value.M34,
            (float)value.M41, (float)value.M42, (float)value.M43, (float)value.M44);
    }

    private static float ReadPositiveFloatProperty(object instance, string propertyName, float fallback)
    {
        return TryReadDoubleProperty(instance, propertyName, out var value) && value > 0
            ? (float)value
            : fallback;
    }

    private static Matrix4x4 ReadTransform3DPropertyOrIdentity(object instance, string propertyName)
    {
        return TryGetPropertyValue(instance, propertyName, out var transform)
            && transform != null
            && TryReadTransform3D(transform, out var matrix)
                ? matrix
                : Matrix4x4.Identity;
    }

    private static bool TryReadTransform3D(object transform, out Matrix4x4 matrix)
    {
        if (TryGetPropertyValue(transform, "Value", out var value) && value != null)
        {
            return TryReadMatrix3D(value, out matrix);
        }

        return TryReadMatrix3D(transform, out matrix);
    }

    private static bool TryReadMatrix3D(object matrixValue, out Matrix4x4 matrix)
    {
        if (!TryReadDoubleProperty(matrixValue, "M11", out var m11)
            || !TryReadDoubleProperty(matrixValue, "M12", out var m12)
            || !TryReadDoubleProperty(matrixValue, "M13", out var m13)
            || !TryReadDoubleProperty(matrixValue, "M14", out var m14)
            || !TryReadDoubleProperty(matrixValue, "M21", out var m21)
            || !TryReadDoubleProperty(matrixValue, "M22", out var m22)
            || !TryReadDoubleProperty(matrixValue, "M23", out var m23)
            || !TryReadDoubleProperty(matrixValue, "M24", out var m24)
            || !TryReadDoubleProperty(matrixValue, "M31", out var m31)
            || !TryReadDoubleProperty(matrixValue, "M32", out var m32)
            || !TryReadDoubleProperty(matrixValue, "M33", out var m33)
            || !TryReadDoubleProperty(matrixValue, "M34", out var m34)
            || !TryReadDoubleProperty(matrixValue, "M44", out var m44))
        {
            matrix = Matrix4x4.Identity;
            return false;
        }

        var hasM41 = TryReadDoubleProperty(matrixValue, "M41", out var m41);
        var hasM42 = TryReadDoubleProperty(matrixValue, "M42", out var m42);
        var hasM43 = TryReadDoubleProperty(matrixValue, "M43", out var m43);

        if (!hasM41 && !TryReadDoubleProperty(matrixValue, "OffsetX", out m41))
        {
            m41 = 0;
        }

        if (!hasM42 && !TryReadDoubleProperty(matrixValue, "OffsetY", out m42))
        {
            m42 = 0;
        }

        if (!hasM43 && !TryReadDoubleProperty(matrixValue, "OffsetZ", out m43))
        {
            m43 = 0;
        }

        matrix = new Matrix4x4(
            (float)m11, (float)m12, (float)m13, (float)m14,
            (float)m21, (float)m22, (float)m23, (float)m24,
            (float)m31, (float)m32, (float)m33, (float)m34,
            (float)m41, (float)m42, (float)m43, (float)m44);
        return true;
    }

    private static bool TryReadViewportBounds(object viewportVisual, out Rect bounds)
    {
        if (TryGetPropertyValue(viewportVisual, "Viewport", out var viewport)
            && viewport != null
            && TryReadRect(viewport, out bounds)
            && IsUsableBounds(bounds))
        {
            return true;
        }

        foreach (var propertyName in new[] { "Bounds", "ContentBounds", "DescendantBounds", "VisualContentBounds" })
        {
            if (TryGetPropertyValue(viewportVisual, propertyName, out var boundsValue)
                && boundsValue != null
                && TryReadRect(boundsValue, out bounds)
                && IsUsableBounds(bounds))
            {
                return true;
            }
        }

        if (TryGetPropertyValue(viewportVisual, "RenderSize", out var renderSize)
            && renderSize != null
            && TryReadSize(renderSize, out var width, out var height)
            && width > 0
            && height > 0)
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadVector3Collection(object collection, out Vector3[] values)
    {
        var result = new List<Vector3>();
        foreach (var item in EnumerateCollection(collection))
        {
            if (item != null && TryReadVector3(item, out var vector))
            {
                result.Add(vector);
            }
        }

        values = result.ToArray();
        return values.Length > 0;
    }

    private static bool TryReadIntCollection(object collection, out int[] values)
    {
        var result = new List<int>();
        foreach (var item in EnumerateCollection(collection))
        {
            if (TryConvertToInt(item, out var value))
            {
                result.Add(value);
            }
        }

        values = result.ToArray();
        return values.Length > 0;
    }

    private static IEnumerable<object?> EnumerateCollection(object collection)
    {
        if (collection is string)
        {
            yield break;
        }

        if (TryReadIntProperty(collection, "Count", out var count) && count > 0)
        {
            var indexer = FindIndexer(collection.GetType());
            if (indexer != null)
            {
                for (var i = 0; i < count; i++)
                {
                    yield return indexer(collection, i);
                }

                yield break;
            }
        }

        if (collection is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }

    private static bool TryReadVector3(object value, out Vector3 vector)
    {
        if (value is Vector3 vectorValue)
        {
            vector = vectorValue;
            return true;
        }

        if (TryReadDoubleProperty(value, "X", out var x)
            && TryReadDoubleProperty(value, "Y", out var y)
            && TryReadDoubleProperty(value, "Z", out var z))
        {
            vector = new Vector3((float)x, (float)y, (float)z);
            return true;
        }

        vector = default;
        return false;
    }

    private static bool TryReadColorVector4(object value, out Vector4 color)
    {
        if (value is Vector4 vectorValue)
        {
            color = vectorValue;
            return true;
        }

        if (TryReadDoubleProperty(value, "R", out var r)
            && TryReadDoubleProperty(value, "G", out var g)
            && TryReadDoubleProperty(value, "B", out var b))
        {
            var alpha = TryReadDoubleProperty(value, "A", out var a) ? a : 255;
            if (r > 1 || g > 1 || b > 1 || alpha > 1)
            {
                r /= 255;
                g /= 255;
                b /= 255;
                alpha /= 255;
            }

            color = new Vector4(
                (float)Math.Clamp(r, 0, 1),
                (float)Math.Clamp(g, 0, 1),
                (float)Math.Clamp(b, 0, 1),
                (float)Math.Clamp(alpha, 0, 1));
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryReadRect(object value, out Rect bounds)
    {
        if (value is Rect rect)
        {
            bounds = rect;
            return true;
        }

        if (TryReadDoubleProperty(value, "X", out var x)
            && TryReadDoubleProperty(value, "Y", out var y)
            && TryReadDoubleProperty(value, "Width", out var width)
            && TryReadDoubleProperty(value, "Height", out var height))
        {
            bounds = new Rect(x, y, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadSize(object value, out double width, out double height)
    {
        width = 0;
        height = 0;

        if (value is Size size)
        {
            width = size.Width;
            height = size.Height;
            return true;
        }

        return TryReadDoubleProperty(value, "Width", out width)
            && TryReadDoubleProperty(value, "Height", out height);
    }

    private static bool IsUsableBounds(Rect bounds)
    {
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        return TryConvertToDouble(propertyValue, out value);
    }

    private static bool TryReadIntProperty(object instance, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        return TryConvertToInt(propertyValue, out value);
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        var indexer = type.GetProperty("Item", MemberFlags, binder: null, returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer != null)
        {
            return (instance, index) => indexer.GetValue(instance, new object[] { index });
        }

        var getter = type.GetMethod("get_Item", MemberFlags, binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (getter != null)
        {
            return (instance, index) => getter.Invoke(instance, new object[] { index });
        }

        return null;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                result = (int)uintValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + suffix, StringComparison.Ordinal) ?? false);
    }

    private readonly record struct MaterialDescriptor(
        Vector4 DiffuseColor,
        Vector3 SpecularColor,
        float Shininess,
        Vector3 AmbientColor,
        float Opacity)
    {
        public static MaterialDescriptor Default { get; } = new(
            Vector4.One,
            new Vector3(0.2f, 0.2f, 0.2f),
            32f,
            new Vector3(0.2f, 0.2f, 0.2f),
            1f);

        public MaterialDescriptor Merge(MaterialDescriptor next)
        {
            return new MaterialDescriptor(
                next.DiffuseColor != Vector4.One ? next.DiffuseColor : DiffuseColor,
                next.SpecularColor != new Vector3(0.2f, 0.2f, 0.2f) ? next.SpecularColor : SpecularColor,
                next.Shininess != 32f ? next.Shininess : Shininess,
                next.AmbientColor != new Vector3(0.2f, 0.2f, 0.2f) ? next.AmbientColor : AmbientColor,
                next.Opacity != 1f ? next.Opacity : Opacity);
        }
    }
}

public readonly record struct WpfViewport3DReplayData(
    global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload Payload,
    Matrix4x4 Projection,
    Matrix4x4 View,
    global::ProGPU.Scene.Rect Viewport);
