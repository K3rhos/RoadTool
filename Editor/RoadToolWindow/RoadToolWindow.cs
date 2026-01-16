using System;
using System.Collections.Generic;
using Sandbox;
using Editor;

namespace RedSnail.RoadTool.Editor;

public partial class RoadToolWindow : WidgetWindow
{
	private RoadComponent _targetComponent;
	private static bool _isClosed;

	private ControlWidget _inTangentControl;
	private ControlWidget _outTangentControl;

	private bool _inTangentSelected;
	private bool _outTangentSelected;
	private bool _draggingOutNewPoint;
	private bool _moveInProgress;

	private List<Vector3> _polyLine = [];
	private IDisposable _movementUndoScope;



	public RoadToolWindow()
	{
		ContentMargins = 0;

		Layout = Layout.Column();

		MaximumWidth = 500;
		MinimumWidth = 300;

		Rebuild();
	}



	public void OnDisabled()
	{

	}



	public void OnUpdate()
	{
		if (!_targetComponent.IsValid())
			return;

		DrawGizmos();
	}



	public void OnSelectionChanged(RoadComponent _Spline)
	{
		_targetComponent = _Spline;

		Rebuild();
	}
}
