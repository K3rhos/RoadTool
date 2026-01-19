# 🌍Overview

This tool is a work in progress procedural road system designed to avoid the need for manual 3D road modeling/mapping.

The road layout is controlled by an editable spline, allowing you to easily adjust curves position and direction to fit your environment. No premade road model is required, the shape of the road is automatically generated using customizable parameters. (Width, Material, ...)

This system also procedurally handles sidewalks, decals, and streetlights placement, removing the need to manually place props along the road. All elements adapt dynamically based on the road’s configuration.

**NOTE: Assets shown in some of the screenshots below (e.g. Materials, Textures...) are not featured in this repository**

<img width="2507" height="1282" alt="Overview" src="https://github.com/user-attachments/assets/d4dfa537-9985-4e69-8334-045d6a752c5a" />

https://github.com/user-attachments/assets/5bdcd072-2cb1-48eb-b8d4-83d65eb1ebce

# 🔧Features

- General features & Optimization
<img width="741" height="252" alt="General" src="https://github.com/user-attachments/assets/05b87295-5a36-4dad-abb6-051ad6844c02" />

- Road generation
<img width="730" height="239" alt="Road" src="https://github.com/user-attachments/assets/30bc1372-c7c6-42fe-ac11-023fcddf08d1" />

- Road lanes
<img width="736" height="320" alt="Lanes" src="https://github.com/user-attachments/assets/8db013c9-6a44-4cc7-b6a4-6edd5cfad3e9" />

- Sidewalk along the road
<img width="736" height="239" alt="Sidewalk" src="https://github.com/user-attachments/assets/8e9a68f2-551a-4d7d-a335-8091d3f7e79d" />

- Road surface decals
<img width="738" height="360" alt="Decals" src="https://github.com/user-attachments/assets/c1766294-bcff-49dc-8eec-35cbd199c448" />

- Lampposts auto generated along the road
<img width="732" height="371" alt="Lampposts" src="https://github.com/user-attachments/assets/58aaa989-0a56-4b74-a535-40ed194a81e6" />

# 📀Setup

## Road texture
I really recommend using a triplanar shader for the road texture otherwise you will still face some issue with "seems" at some places when using a standard shader.

Example:

<img width="1353" height="692" alt="Road Material Standard Shader" src="https://github.com/user-attachments/assets/610a975a-c0d9-431b-ad79-1fef0c2c6eaa" />
<img width="1353" height="692" alt="Road Material Triplanar Shader" src="https://github.com/user-attachments/assets/4a637e44-5758-4ff5-8fa5-b727889f68f2" />

## Sidewalk texture
The sidewalk texture can be just a regular seamless texture, however if you want to use a sidewalk texture with an actual sidewalk border, here is how it should be setup for the border to face the road properly:

<img width="512" height="512" alt="Sidewalk Texture" src="https://github.com/user-attachments/assets/d80e871d-14d4-4ede-8cc8-c933aa98ebfd" />

Demo:

<img width="2458" height="1265" alt="Sidewalk Texture Demo" src="https://github.com/user-attachments/assets/f5883a28-be8e-4178-8806-1f0c402e0340" />

Demo (With a proper texture):

<img width="2405" height="1238" alt="Sidewalk Texture Demo (With a proper texture)" src="https://github.com/user-attachments/assets/72b25a7f-bcce-442c-82b4-c52e6f6c8ece" />

# 📜Credits

- [Facepunch](https://facepunch.com/) ([Spline Tools](https://sbox.game/facepunch/splinetools) was a useful resources to start making this tool)
