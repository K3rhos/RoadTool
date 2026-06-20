using Sandbox;

public sealed class PlayerParentHandler : Component
{
	private GameObject m_LastVehicle;

	// This only seems to be executed on the host, but that's actually pretty good design
	// But that mean instead of doing TakeOwnership we have to assign the owership from the host side to the correct client connection
	protected override void OnParentChanged(GameObject _OldParent, GameObject _NewParent)
	{
		if (m_LastVehicle.IsValid() && m_LastVehicle != _OldParent.Root)
		{
			m_LastVehicle.Tags.Remove("last_vehicle");
			
			var rigidbody = m_LastVehicle.GetComponent<Rigidbody>();
			
			if (rigidbody.IsValid())
				rigidbody.EnhancedCcd = false;
			
			m_LastVehicle.Network.Refresh();
		}

		if (_NewParent.Tags.Has("vehicle"))
		{
			m_LastVehicle = _NewParent.Root;
			m_LastVehicle.Tags.Add("last_vehicle");
			
			var rigidbody = m_LastVehicle.GetComponent<Rigidbody>();
			
			if (rigidbody.IsValid())
				rigidbody.EnhancedCcd = true;

			m_LastVehicle.Network.Refresh();
			
			m_LastVehicle.Network.AssignOwnership(Network.Owner);
		}
	}	
}
