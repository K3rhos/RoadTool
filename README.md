# 🌍Overview

This tool is a work in progress procedural road system designed to avoid the need for manual 3D road modeling/mapping.

The road layout is controlled by an editable spline, allowing you to easily adjust curves position and direction to fit your environment. No premade road model is required, the shape of the road is automatically generated using customizable parameters. (Width, Material, ...)

This system also procedurally handles sidewalks, decals, and streetlights placement, removing the need to manually place props along the road. All elements adapt dynamically based on the road’s configuration.

**NOTE: Assets shown in some of the screenshots/videos (e.g. Models, Materials, Textures...) are not featured with this repository**

<img width="2507" height="1282" alt="Overview" src="https://i.imgur.com/nLGyZxq.png" />

https://github.com/user-attachments/assets/5bdcd072-2cb1-48eb-b8d4-83d65eb1ebce

# 🔧Features

- General features & Optimization
<img width="741" height="252" alt="General" src="https://i.imgur.com/Tr3wxdD.png" />

- Road generation
<img width="730" height="239" alt="Road" src="https://i.imgur.com/d2nLVv7.png" />

- Road lanes
<img width="736" height="320" alt="Lanes" src="https://i.imgur.com/GarZIuW.png" />

- Sidewalk along the road
<img width="736" height="239" alt="Sidewalk" src="https://i.imgur.com/setuCiI.png" />

- Road surface decals
<img width="738" height="360" alt="Decals" src="https://i.imgur.com/ZJtCmJl.png" />

- Lampposts auto generated along the road
<img width="732" height="371" alt="Lampposts" src="https://i.imgur.com/MljcqXP.png" />

# 📀Setup

## Road texture
I really recommend using a triplanar shader for the road texture otherwise you will still face some issue with "seems" at some places when using a standard shader.

Example:

<img width="1353" height="692" alt="Road Material Standard Shader" src="https://i.imgur.com/iCTVQvE.png" />
<img width="1353" height="692" alt="Road Material Triplanar Shader" src="https://i.imgur.com/K0PMmA6.png" />

## Sidewalk texture
The sidewalk texture can be just a regular seamless texture, however if you want to use a sidewalk texture with an actual sidewalk border, here is how it should be setup for the border to face the road properly:

<img width="512" height="512" alt="Sidewalk Texture" src="https://i.imgur.com/w0eZxt1.jpeg" />

Demo:

<img width="2458" height="1265" alt="Sidewalk Texture Demo" src="https://i.imgur.com/Gq3Dje7.png" />

Demo (With a proper texture):

<img width="2405" height="1238" alt="Sidewalk Texture Demo (With a proper texture)" src="https://i.imgur.com/0WNm38U.png" />

# 📜Credits

- [Facepunch](https://facepunch.com/) ([Spline Tools](https://sbox.game/facepunch/splinetools) was a useful resources to start making this tool)
