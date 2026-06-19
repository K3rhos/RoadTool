using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

public sealed class EntityFade : Component
{
	private float m_FadeTimer;
	private IEnumerable<ModelRenderer> m_Renderers = [];

	[Property] private float Duration { get; set; } = 0.5f;
	
	protected override void OnAwake()
	{
		m_Renderers = GetComponentsInChildren<ModelRenderer>(true);

		_ = FadeAlpha();
	}
	
	private async Task FadeAlpha()
	{
		while (m_FadeTimer < Duration)
		{
			m_FadeTimer += Time.Delta;
			
			float alpha = (m_FadeTimer / Duration).Clamp(0.0f, 1.0f);
			
			foreach (var renderer in m_Renderers)
				renderer.Tint = renderer.Tint.WithAlpha(alpha);

			await Task.Frame();
		}
	}
}
