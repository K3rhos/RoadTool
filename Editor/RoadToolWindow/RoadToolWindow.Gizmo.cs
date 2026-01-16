using Sandbox;

namespace RedSnail.RoadTool.Editor;

public partial class RoadToolWindow
{
	private const float GIZMO_BOX_SIZE = 2.0f;
	private const float LINE_THICKNESS = 2.0f;
	private const float TANGENT_LINE_THICKNESS = 0.8f;



	private void DrawGizmos()
	{
		using (Gizmo.Scope("road_editor", _targetComponent.WorldTransform))
		{
			DrawSplineSegments();
			DrawPositionGizmo();
			DrawPointControls();
		}
	}



	private void DrawSplineSegments()
	{
		_targetComponent.Spline.ConvertToPolyline(ref _polyLine);

		for (var i = 0; i < _polyLine.Count - 1; i++)
		{
			DrawSegment(i, _polyLine[i], _polyLine[i + 1]);
		}
	}



	private void DrawSegment(int index, Vector3 start, Vector3 end)
	{
		using (Gizmo.Scope("segment" + index))
		using (Gizmo.Hitbox.LineScope())
		{
			Gizmo.Draw.LineThickness = LINE_THICKNESS;
			Gizmo.Hitbox.AddPotentialLine(start, end, LINE_THICKNESS * 2f);
			Gizmo.Draw.Line(start, end);

			if (Gizmo.IsHovered && Gizmo.HasMouseFocus)
			{
				HandleSegmentHover(start, end);
			}
		}
	}



	private void HandleSegmentHover(Vector3 start, Vector3 end)
	{
		Gizmo.Draw.Color = Color.Cyan;

		if (!new Line(start, end).ClosestPoint(Gizmo.CurrentRay.ToLocal(Gizmo.Transform),
			out Vector3 pointOnLine, out _))
			return;

		var hoverSample = _targetComponent.Spline.SampleAtClosestPosition(pointOnLine);
		DrawHoverHandle(pointOnLine, hoverSample.Tangent);

		if (Gizmo.HasClicked && Gizmo.Pressed.This)
		{
			InsertPointAtHover(hoverSample.Distance);
		}
	}



	private void DrawHoverHandle(Vector3 position, Vector3 tangent)
	{
		using (Gizmo.Scope("hover_handle", new Transform(position, Rotation.LookAt(tangent))))
		using (Gizmo.GizmoControls.PushFixedScale())
		{
			Gizmo.Draw.SolidBox(BBox.FromPositionAndSize(Vector3.Zero, GIZMO_BOX_SIZE));
		}
	}



	private void InsertPointAtHover(float distance)
	{
		using (CreateUndoScope("Added spline point"))
		{
			var newPointIndex = _targetComponent.Spline.AddPointAtDistance(distance, true);
			SelectedPointIndex = newPointIndex;
			_inTangentSelected = false;
			_outTangentSelected = false;
		}
	}



	private void DrawPositionGizmo()
	{
		var gizmoPosition = CalculateGizmoPosition();

		if (!Gizmo.IsShiftPressed)
		{
			_draggingOutNewPoint = false;
		}

		using (Gizmo.Scope("position", new Transform(gizmoPosition)))
		{
			HandlePositionControl();
		}
	}



	private Vector3 CalculateGizmoPosition()
	{
		var position = _selectedPoint.Position;

		if (_inTangentSelected)
			position += _selectedPoint.In;
		else if (_outTangentSelected)
			position += _selectedPoint.Out;

		return position;
	}



	private void HandlePositionControl()
	{
		_moveInProgress = false;

		if (Gizmo.Control.Position("spline_control_", Vector3.Zero, out var delta))
		{
			_moveInProgress = true;
			_movementUndoScope ??= CreateUndoScope("Moved spline point");

			if (_inTangentSelected)
				MoveSelectedPointInTangent(delta);
			else if (_outTangentSelected)
				MoveSelectedPointOutTangent(delta);
			else
				HandlePointMove(delta);
		}

		if (!_moveInProgress && Gizmo.WasLeftMouseReleased)
		{
			_movementUndoScope?.Dispose();
			_movementUndoScope = null;
		}
	}



	private void HandlePointMove(Vector3 delta)
	{
		if (Gizmo.IsShiftPressed && !_draggingOutNewPoint)
		{
			_draggingOutNewPoint = true;
			var currentPoint = _targetComponent.Spline.GetPoint(SelectedPointIndex);
			_targetComponent.Spline.InsertPoint(SelectedPointIndex + 1, currentPoint);
			SelectedPointIndex++;
		}
		else
		{
			MoveSelectedPoint(delta);
		}
	}



	private void DrawPointControls()
	{
		var spline = _targetComponent.Spline;

		for (var i = 0; i < spline.PointCount; i++)
		{
			if (spline.IsLoop && i == spline.SegmentCount)
				continue;

			var point = spline.GetPoint(i);
			DrawPointControl(i, point);
		}
	}



	private void DrawPointControl(int index, Spline.Point point)
	{
		using (Gizmo.Scope("point_controls" + index, new Transform(point.Position)))
		{
			Gizmo.Draw.IgnoreDepth = true;
			DrawPointPositionHandle(index);

			if (SelectedPointIndex == index)
			{
				DrawTangentHandles(point);
			}
		}
	}



	private void DrawPointPositionHandle(int index)
	{
		using (Gizmo.Scope("position"))
		using (Gizmo.GizmoControls.PushFixedScale())
		{
			Gizmo.Hitbox.DepthBias = 0.1f;
			Gizmo.Hitbox.BBox(BBox.FromPositionAndSize(Vector3.Zero, GIZMO_BOX_SIZE));

			bool isSelected = index == SelectedPointIndex && !_inTangentSelected && !_outTangentSelected;

			if (Gizmo.IsHovered || isSelected)
			{
				Gizmo.Draw.Color = Color.Cyan;
			}

			Gizmo.Draw.SolidBox(BBox.FromPositionAndSize(Vector3.Zero, GIZMO_BOX_SIZE));

			if (Gizmo.HasClicked && Gizmo.Pressed.This)
			{
				SelectPoint(index);
			}
		}
	}



	private void DrawTangentHandles(Spline.Point point)
	{
		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.LineThickness = TANGENT_LINE_THICKNESS;

		DrawTangentHandle("in_tangent", point.In, -point.In, ref _inTangentSelected, ref _outTangentSelected);
		DrawTangentHandle("out_tangent", point.Out, -point.Out, ref _outTangentSelected, ref _inTangentSelected);
	}



	private void DrawTangentHandle(string name, Vector3 offset, Vector3 lineStart, ref bool thisSelected, ref bool otherSelected)
	{
		using (Gizmo.Scope(name, new Transform(offset)))
		{
			bool isMirroredOrAuto = _selectedPointTangentMode is HandleModeTemp.Mirrored or HandleModeTemp.Auto;
			if (isMirroredOrAuto && (thisSelected || otherSelected))
			{
				Gizmo.Draw.Color = Color.Cyan;
			}

			Gizmo.Draw.Line(lineStart, Vector3.Zero);

			if (_selectedPointTangentMode != HandleModeTemp.Linear)
			{
				DrawTangentBox(ref thisSelected, ref otherSelected);
			}
		}
	}



	private void DrawTangentBox(ref bool thisSelected, ref bool otherSelected)
	{
		using (Gizmo.GizmoControls.PushFixedScale())
		{
			Gizmo.Hitbox.DepthBias = 0.1f;
			Gizmo.Hitbox.BBox(BBox.FromPositionAndSize(Vector3.Zero, GIZMO_BOX_SIZE));

			if (Gizmo.IsHovered || thisSelected)
			{
				Gizmo.Draw.Color = Color.Cyan;
			}

			Gizmo.Draw.SolidBox(BBox.FromPositionAndSize(Vector3.Zero, GIZMO_BOX_SIZE));

			if (Gizmo.HasClicked && Gizmo.Pressed.This)
			{
				thisSelected = true;
				otherSelected = false;
			}
		}
	}
}
