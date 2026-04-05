using System;
using Editor;
using System.Linq;
using Sandbox;
using RedSnail.RoadTool;

namespace RedSnail.RoadTool.Editor;

/// <summary>
/// Editor tool used to deform the terrain storage to align with a selected road spline.
/// Provides controls for falloff radius, sampling precision, and height offsets.
/// </summary>
[Title("Terrain")]
[Icon("landscape")]
[Group("1")]
[Order(0)]
public class TerrainEditorTool : EditorTool
{
	public override Widget CreateToolSidebar()
	{
		ToolSidebarWidget sidebar = new ToolSidebarWidget();
		sidebar.AddTitle("Terrain", "landscape");

		var selection = SceneEditorSession.Active.Selection.FirstOrDefault();
		Component targetComponent = null;

		if (selection is RoadComponent r) targetComponent = r;
		else if (selection is RoadIntersectionComponent i) targetComponent = i;
		else if (selection is GameObject go)
		{
			targetComponent = go.Components.Get<RoadComponent>() ?? (Component)go.Components.Get<RoadIntersectionComponent>();
		}

		if (targetComponent.IsValid())
		{
			var serialized = targetComponent.GetSerialized();

			Layout propertiesGroup = sidebar.AddGroup("Properties");
			var varProperties = targetComponent is RoadComponent
				? new[] { "TerrainFalloffRadius", "TerrainStepPrecision", "TerrainHeightOffset" }
				: new[] { "TerrainFalloffRadius", "TerrainHeightOffset" };


			foreach (var propName in varProperties)
			{
				AddPropertyControl(propertiesGroup, serialized.GetProperty(propName));
			}
			propertiesGroup.Add(new Button("Apply to the Ground", "landscape") { Clicked = AlignTerrainToRoad });

			Layout texGroup = sidebar.AddGroup("Texture");
			var texProperties = new[] { "TerrainEdgeRadius", "TerrainTargetLayer", "TerrainTextureNoise", "TerrainEdgeMaterials", "TerrainEdgeBlendGradient" };

			foreach (var propName in texProperties)
			{
				AddPropertyControl(texGroup, serialized.GetProperty(propName));
			}
			texGroup.Add(new Button("Apply Materials", "palette") { Clicked = PaintRoadMaterials });
		}
		else
		{
			Layout componente = sidebar.AddGroup("Componentes");
			componente.Add(new Label("Select a route to edit"));
		}

		// Flexible space to push everything to the top
		sidebar.Layout.AddStretchCell();

		return sidebar;
	}

	private void AddPropertyControl(Layout layout, SerializedProperty prop)
	{
		if (prop == null) return;

		var propLayout = layout.AddColumn();
		propLayout.Spacing = 2;
		propLayout.Margin = new Sandbox.UI.Margin(0, 4, 0, 4);
		propLayout.Add(ControlSheet.CreateLabel(prop));
		propLayout.Add(ControlWidget.Create(prop));
	}


	/// <summary>
	/// This method applies to the selected RoadComponent.
	/// </summary>
	public static void AlignTerrainToRoad()
	{
		var selection = SceneEditorSession.Active.Selection.FirstOrDefault();
		RoadComponent road = null;
		RoadIntersectionComponent intersection = null;

		if (selection is RoadComponent r) road = r;
		else if (selection is RoadIntersectionComponent i) intersection = i;
		else if (selection is GameObject go)
		{
			road = go.Components.Get<RoadComponent>();
			intersection = go.Components.Get<RoadIntersectionComponent>();
		}

		if (road == null && intersection == null)
		{
			Log.Warning("RoadTool: Please select a Road or Intersection to use this tool.");
			return;
		}

		var terrain = SceneEditorSession.Active.Scene.GetAllComponents<Terrain>().FirstOrDefault();
		if (terrain == null) return;

		// 1. Capture state BEFORE
		var storage = terrain.Storage;
		var oldHeightMap = new ushort[storage.HeightMap.Length];
		Array.Copy(storage.HeightMap, oldHeightMap, oldHeightMap.Length);

		// 2. Execute modification based on type
		if (road.IsValid()) road.AdaptTerrainToRoad();
		else if (intersection.IsValid()) intersection.AdaptTerrainToIntersection();

		// 3. Capture state AFTER
		var newHeightMap = new ushort[storage.HeightMap.Length];
		Array.Copy(storage.HeightMap, newHeightMap, newHeightMap.Length);

		// 4. Register in editor history
		var targetTerrain = terrain;
		var targetStorage = storage;

		SceneEditorSession.Active.AddUndo("Align Terrain to Road",
			undo: () =>
			{
				if (!targetTerrain.IsValid()) return;
				targetStorage.HeightMap = oldHeightMap;
				targetStorage.StateHasChanged();
				targetTerrain.Create();
				targetTerrain.SyncGPUTexture();
			},
			redo: () =>
			{
				if (!targetTerrain.IsValid()) return;
				targetStorage.HeightMap = newHeightMap;
				targetStorage.StateHasChanged();
				targetTerrain.Create();
				targetTerrain.SyncGPUTexture();
			});
	}

	public static void PaintRoadMaterials()
	{
		var selection = SceneEditorSession.Active.Selection.FirstOrDefault();
		RoadComponent road = null;
		RoadIntersectionComponent intersection = null;

		if (selection is RoadComponent r) road = r;
		else if (selection is RoadIntersectionComponent i) intersection = i;
		else if (selection is GameObject go)
		{
			road = go.Components.Get<RoadComponent>();
			intersection = go.Components.Get<RoadIntersectionComponent>();
		}

		if (road == null && intersection == null) return;

		var terrain = SceneEditorSession.Active.Scene.GetAllComponents<Terrain>().FirstOrDefault();
		if (terrain == null) return;

		var storage = terrain.Storage;
		var oldControlMap = storage.ControlMap.ToArray();

		if (road.IsValid()) road.PaintTerrainToRoad();
		else if (intersection.IsValid()) intersection.PaintTerrainToIntersection();

		var newControlMap = storage.ControlMap.ToArray();

		var targetTerrain = terrain;
		var targetStorage = storage;

		SceneEditorSession.Active.AddUndo("Paint Terrain Materials",
			undo: () =>
			{
				if (!targetTerrain.IsValid()) return;
				// Re-copy to avoid reference issues
				targetStorage.ControlMap = oldControlMap.ToArray();
				targetStorage.StateHasChanged();
				targetTerrain.SyncGPUTexture();
			},
			redo: () =>
			{
				if (!targetTerrain.IsValid()) return;
				// Re-copy to avoid reference issues
				targetStorage.ControlMap = newControlMap.ToArray();
				targetStorage.StateHasChanged();
				targetTerrain.SyncGPUTexture();
			});
	}
}
