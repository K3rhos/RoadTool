using Sandbox;

public sealed class PlayerParentHandler : Component
{
	private GameObject m_LastVehicle;

	protected override void OnParentChanged(GameObject _OldParent, GameObject _NewParent)
	{
		if (m_LastVehicle.IsValid() && m_LastVehicle != _OldParent.Root)
		{
			m_LastVehicle.Tags.Remove("last_vehicle");
			m_LastVehicle.Network.Refresh();
		}

		if (_NewParent.Tags.Has("vehicle"))
		{
			m_LastVehicle = _NewParent.Root;
			m_LastVehicle.Tags.Add("last_vehicle");
			m_LastVehicle.Network.Refresh();
		}
	}	
}
