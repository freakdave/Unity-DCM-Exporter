# Unity-DCM-Exporter
Rudimentary DCM (Dreamcast Mesh) file exporter for Unity based on the DCM specification from https://gitlab.com/simulant/community/dcm

This Unity Editor extension provides functionality to export 3D models from Unity into the DCM (Dreamcast Mesh) format, which is used for Sega Dreamcast game development.
Tested with Unity 6000.0.39f1

 # Features:
 * Exports mesh data with support for positions, UVs, colors, and normals
 * Applies necessary mesh transformations for compatibility with the Dreamcast renderer
 * Supports proper coordinate system conversion between Unity and Dreamcast

# Usage:
- In your Unity project, place this script in Assets/Editor  
- Select the GameObject (with a MeshRenderer) that you want to export  
- Click File -> Export -> DCM (Dreamcast Mesh)
- Configure as needed (WIP) and click the 'Export DCM' button

# Warning:
For now, only meshes with 1 submesh are supported. 

# Misc:
If you want to improve this tool: pull requests are welcome!
