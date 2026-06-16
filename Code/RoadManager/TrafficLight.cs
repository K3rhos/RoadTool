using Sandbox;

namespace RedSnail.RoadTool;

public sealed class TrafficLight : Component, ITrafficLightEvents
{
	private ModelRenderer m_ModelRenderer;
	
	[Property] private Material GreenLight { get; set; }
	
	
	
	protected override void OnAwake()
	{
		m_ModelRenderer = GetComponent<ModelRenderer>(true);
	}
	
	
	
	void ITrafficLightEvents.OnTrafficLightGoesRed()
	{
		if (!m_ModelRenderer.IsValid())
			return;
		
		m_ModelRenderer.ClearMaterialOverrides();
	}
	
	
	void ITrafficLightEvents.OnTrafficLightGoesGreen()
	{
		if (!m_ModelRenderer.IsValid())
			return;
		
		m_ModelRenderer.MaterialOverride = GreenLight;
	}
}
