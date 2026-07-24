#!/usr/bin/env bash

progpu_preview_runtime_package_ids=(
  ProGPU.Backend
  ProGPU.Text.Shaping
  ProGPU.DirectX
  ProGPU.Transpiler
  ProGPU.Compute
  ProGPU.Vector
  ProGPU.Text
  ProGPU.Scene
  ProGPU.Layout
  ProGPU.Virtualization
  ProGPU.WinUI
  ProGPU.Avalonia
  ProGPU.SkiaSharp
  ProGPU.System.Drawing.Common
  LibreWPF.Interop
)

progpu_preview_package_ids=(
  LibreWPF.Transport
  "${progpu_preview_runtime_package_ids[@]}"
  LibreWPF.ProGPU
  LibreWPF.Sdk
)
