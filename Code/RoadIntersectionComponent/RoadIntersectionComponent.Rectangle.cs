using System;
using Sandbox;

namespace RedSnail.RoadTool;

[Flags]
public enum RectangleExit
{
	None = 0,
	North = 1 << 0, // +Forward
	East  = 1 << 1, // +Right
	South = 1 << 2, // -Forward
	West  = 1 << 3  // -Right
}

public partial class RoadIntersectionComponent
{
	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Width { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500.0f;
	[Property, ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private float Length { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = 500.0f;
	[Property(Title = "Exits"), ShowIf(nameof(Shape), IntersectionShape.Rectangle)] private RectangleExit RectangleExits { get; set { field = value; m_MeshBuilder?.IsDirty = true; } } = RectangleExit.None;
	
	
	
	private void BuildRectangleRoad()
	{
		int exitCount = 0;
		
		if (RectangleExits.HasFlag(RectangleExit.North)) exitCount++;
		if (RectangleExits.HasFlag(RectangleExit.East)) exitCount++;
		if (RectangleExits.HasFlag(RectangleExit.South)) exitCount++;
		if (RectangleExits.HasFlag(RectangleExit.West)) exitCount++;

		m_MeshBuilder.InitSubmesh
		(
			"intersection_road",
			(1 + exitCount) * 4,
			(1 + exitCount) * 6,
			RoadMaterial ?? Material.Load("materials/dev/reflectivity_30.vmat"),
			_HasCollision: true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;
		Vector3 up = WorldRotation.Up;

		float hw = Width * 0.5f;
		float hl = Length * 0.5f;
		
		Vector3 pSW = -right * hw - forward * hl; // South West
		Vector3 pNW = -right * hw + forward * hl; // North West
		Vector3 pNE =  right * hw + forward * hl; // North East
		Vector3 pSE =  right * hw - forward * hl; // South East

		// Main center quad
		float roadU = Width / RoadTextureRepeat;
		float roadV = Length / RoadTextureRepeat;

		m_MeshBuilder.AddQuad("intersection_road", pSE, pNE, pNW, pSW, up, forward,
			new Vector2(0, 0), new Vector2(0, roadV), new Vector2(roadU, roadV), new Vector2(roadU, 0));

		// Road extensions for exits
		if (RectangleExits.HasFlag(RectangleExit.North))
			AddRoadExtension(pNW, pNE, forward);
    
		if (RectangleExits.HasFlag(RectangleExit.South))
			AddRoadExtension(pSE, pSW, -forward);

		if (RectangleExits.HasFlag(RectangleExit.East))
			AddRoadExtension(pNE, pSE, right);

		if (RectangleExits.HasFlag(RectangleExit.West))
			AddRoadExtension(pSW, pNW, -right);
	}
	
	
	
	private void AddRoadExtension(Vector3 _CornerA, Vector3 _CornerB, Vector3 _Direction)
	{
		Vector3 up = WorldRotation.Up;
		
		Vector3 extA = _CornerA + _Direction * SidewalkWidth;
		Vector3 extB = _CornerB + _Direction * SidewalkWidth;
		
		float extU = (_CornerB - _CornerA).Length / RoadTextureRepeat;
		float extV = SidewalkWidth / RoadTextureRepeat;
		
		m_MeshBuilder.AddQuad
		(
			"intersection_road",
			_CornerB, extB, extA, _CornerA,
			up,
			_Direction,
			new Vector2(0, 0), new Vector2(extV, 0), new Vector2(extV, extU), new Vector2(0, extU)
		);
	}



	private void BuildRectangleSidewalk()
	{
		// Calculate how many sides are NOT exits
		int sideCount = 0;
		
		if (!RectangleExits.HasFlag(RectangleExit.North)) sideCount++;
		if (!RectangleExits.HasFlag(RectangleExit.South)) sideCount++;
		if (!RectangleExits.HasFlag(RectangleExit.East)) sideCount++;
		if (!RectangleExits.HasFlag(RectangleExit.West)) sideCount++;
		
		int endCapCount = 0;
		
		// Check every corner for adjacent exits
		if (RectangleExits.HasFlag(RectangleExit.North)) endCapCount += 2; // North exit affects NW and NE corners
		if (RectangleExits.HasFlag(RectangleExit.South)) endCapCount += 2; // South exit affects SW and SE corners
		if (RectangleExits.HasFlag(RectangleExit.East))  endCapCount += 2; // East exit affects NE and SE corners
		if (RectangleExits.HasFlag(RectangleExit.West))  endCapCount += 2; // West exit affects NW and SW corners

		// Each side = 3 Quads (Top, Inner, Outer) = 12 vertices, 18 indices
		int totalVertices = (sideCount * 12) + (4 * 12) + (endCapCount * 4);
		int totalIndices = (sideCount * 18) + (4 * 18) + (endCapCount * 6);

		m_MeshBuilder.InitSubmesh
		(
			"intersection_sidewalk", 
			totalVertices, 
			totalIndices, 
			SidewalkMaterial ?? Material.Load("materials/dev/reflectivity_70.vmat"), 
			true
		);

		Vector3 right = WorldRotation.Right;
		Vector3 forward = WorldRotation.Forward;
		float hw = Width * 0.5f;
		float hl = Length * 0.5f;

		// North (+Forward)
		if (!RectangleExits.HasFlag(RectangleExit.North))
			AddSidewalkStrip(right * hw + forward * hl, -right * hw + forward * hl, forward);

		// South (-Forward)
		if (!RectangleExits.HasFlag(RectangleExit.South))
			AddSidewalkStrip(-right * hw - forward * hl, right * hw - forward * hl, -forward);

		// East (+Right)
		if (!RectangleExits.HasFlag(RectangleExit.East))
			AddSidewalkStrip(right * hw - forward * hl, right * hw + forward * hl, right);

		// West (-Right)
		if (!RectangleExits.HasFlag(RectangleExit.West))
			AddSidewalkStrip(-right * hw + forward * hl, -right * hw - forward * hl, -right);
		
		bool n = RectangleExits.HasFlag(RectangleExit.North);
		bool s = RectangleExits.HasFlag(RectangleExit.South);
		bool e = RectangleExits.HasFlag(RectangleExit.East);
		bool w = RectangleExits.HasFlag(RectangleExit.West);
		
		AddCornerCap(-right * hw - forward * hl, -right, -forward, w, s); // South-West
		AddCornerCap( right * hw - forward * hl,  right, -forward, e, s); // South-East
		AddCornerCap( right * hw + forward * hl,  right,  forward,  e, n); // North-East
		AddCornerCap(-right * hw + forward * hl, -right,  forward,  w, n); // North-West
	}
	
	
	
	private void AddCornerCap(Vector3 _CornerPos, Vector3 _DirA, Vector3 _DirB, bool _SideAIsExit, bool _SideBIsExit)
	{
	    Vector3 up = WorldRotation.Up;
	    float h = SidewalkHeight;
	    float w = SidewalkWidth;
	    
	    Vector3 pCenter = _CornerPos;
	    Vector3 pA = _CornerPos + _DirA * w;
	    Vector3 pB = _CornerPos + _DirB * w;
	    Vector3 pOuter = _CornerPos + (_DirA * w) + (_DirB * w);
	    
	    Vector3 tCenter = pCenter + up * h;
	    Vector3 tA = pA + up * h;
	    Vector3 tB = pB + up * h;
	    Vector3 tOuter = pOuter + up * h;
	    
	    Vector3 cross = Vector3.Cross(_DirA, _DirB);
	    bool flip = Vector3.Dot(cross, up) >= 0;

	    float uW = SidewalkWidth / SidewalkTextureRepeat;
	    float hH = SidewalkHeight / SidewalkTextureRepeat;
	    
	    if (flip)
	    {
		    // Top face
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, tB, tCenter, tA, up, _DirA, 
			    new Vector2(uW, uW), new Vector2(0, uW), new Vector2(0, 0), new Vector2(uW, 0));
       
		    // Outer face A
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tA, pA, pOuter, tOuter, _DirA, _DirB,
			    new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));

		    // Outer face B
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, pOuter, pB, tB, _DirB, -_DirA,
			    new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
	    }
	    else
	    {
		    // Top face
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, tA, tCenter, tB, up, _DirA, 
			    new Vector2(uW, uW), new Vector2(0, uW), new Vector2(0, 0), new Vector2(uW, 0));

		    // Outer face A
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tOuter, pOuter, pA, tA, _DirA, -_DirB,
			    new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));

		    // Outer face B
		    m_MeshBuilder.AddQuad("intersection_sidewalk", tB, pB, pOuter, tOuter, _DirB, _DirA,
			    new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0), new Vector2(0, 0));
	    }
	    
	    // Seal the "A" side if it's an exit
	    if (_SideAIsExit)
	    {
		    if (flip)
			    m_MeshBuilder.AddQuad
			    (
					"intersection_sidewalk",
					tA, tCenter, pCenter, pA,
					-_DirB,
					-_DirA,
					new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0)
			    );
		    else
			    m_MeshBuilder.AddQuad
			    (
				    "intersection_sidewalk",
				    tCenter, tA, pA, pCenter,
				    -_DirB,
				    _DirA,
				    new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0)
			    );
	    }

	    // Seal the "B" side if it's an exit
	    if (_SideBIsExit)
	    {
		    if (flip)
			    m_MeshBuilder.AddQuad
			    (
				    "intersection_sidewalk",
				    tCenter, tB, pB, pCenter,
				    -_DirA,
				    _DirB,
				    new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0)
			    );
		    else
			    m_MeshBuilder.AddQuad
			    (
				    "intersection_sidewalk",
				    tB, tCenter, pCenter, pB,
				    -_DirA,
				    -_DirB,
				    new Vector2(0, 0), new Vector2(0, uW), new Vector2(hH, uW), new Vector2(hH, 0)
			    );
	    }
	}
	
	
	
	private void AddSidewalkStrip(Vector3 _Start, Vector3 _End, Vector3 _Outward)
	{
		Vector3 up = WorldRotation.Up;
		Vector3 forward = (_End - _Start).Normal;

		// Bottom points
		Vector3 s0 = _Start;
		Vector3 s1 = _End;
		Vector3 o0 = s0 + _Outward * SidewalkWidth;
		Vector3 o1 = s1 + _Outward * SidewalkWidth;

		// Top points
		Vector3 t0 = s0 + up * SidewalkHeight;
		Vector3 t1 = s1 + up * SidewalkHeight;
		Vector3 ot0 = o0 + up * SidewalkHeight;
		Vector3 ot1 = o1 + up * SidewalkHeight;

		float stripLen = (_End - _Start).Length;
		float uWidth = SidewalkWidth / SidewalkTextureRepeat;
		float vLen = stripLen / SidewalkTextureRepeat;
		float hHeight = SidewalkHeight / SidewalkTextureRepeat;

		// Top face
		m_MeshBuilder.AddQuad("intersection_sidewalk", ot0, ot1, t1, t0, up, forward,
			new Vector2(uWidth, 0), new Vector2(uWidth, vLen), new Vector2(0, vLen), new Vector2(0, 0));
		
		// Inner face
		Vector3 innerNormal = -_Outward;
		m_MeshBuilder.AddQuad("intersection_sidewalk", t0, t1, s1, s0, innerNormal, forward,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));
		
		// Outer face
		Vector3 outerNormal = _Outward;
		m_MeshBuilder.AddQuad("intersection_sidewalk", ot1, ot0, o0, o1, outerNormal, -forward,
			new Vector2(0, 0), new Vector2(0, vLen), new Vector2(hHeight, vLen), new Vector2(hHeight, 0));
	}
	
	
	
	private Transform GetRectangleExitLocalTransform(RectangleExit _Side, bool _IncludeSidewalk = false)
	{
		Vector3 pos = Vector3.Zero;
		Rotation rot = Rotation.Identity;
		
		switch (_Side)
		{
			case RectangleExit.North: 
				pos += LocalRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				break;
			case RectangleExit.South: 
				pos -= LocalRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(180);
				break;
			case RectangleExit.East:  
				pos += LocalRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(-90);
				break;
			case RectangleExit.West:  
				pos -= LocalRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(90);
				break;
		}

		return new Transform { Position = pos, Rotation = rot };
	}
	
	
	
	private Transform GetRectangleExitTransform(RectangleExit _Side, bool _IncludeSidewalk = false)
	{
		Vector3 pos = WorldPosition;
		Rotation rot = WorldRotation;

		switch (_Side)
		{
			case RectangleExit.North: 
				pos += WorldRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				break;
			case RectangleExit.South: 
				pos -= WorldRotation.Forward * ((Length * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(180);
				break;
			case RectangleExit.East:  
				pos += WorldRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(-90);
				break;
			case RectangleExit.West:  
				pos -= WorldRotation.Right * ((Width * 0.5f) + (_IncludeSidewalk ? SidewalkWidth : 0.0f));
				rot *= Rotation.FromYaw(90);
				break;
		}

		return new Transform { Position = pos, Rotation = rot };
	}
}
