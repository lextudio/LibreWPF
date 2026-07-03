#!/usr/bin/env bash

progpu_preview_runtime_package_ids=(
  ProGPU.Backend
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
  ProGPU.Wpf.Interop
)

progpu_preview_package_ids=(
  Microsoft.DotNet.Wpf.GitHub
  "${progpu_preview_runtime_package_ids[@]}"
  ProGPU.Wpf
  ProGPU.Wpf.Sdk
)
