using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

public sealed class EntityFade : Component
{
	private float m_FadeTimer;
	private bool m_IsFading;
	private IEnumerable<ModelRenderer> m_Renderers = [];

	[Property] private float Duration { get; set; } = 0.5f;
	
	protected override void OnAwake()
	{
		m_Renderers = GetComponentsInChildren<ModelRenderer>(true);

		_ = AsyncFadeIn();
	}

	private async Task AsyncFadeIn()
	{
		if (m_IsFading)
			return;
		
		m_IsFading = true;
		m_FadeTimer = 0;
		
		while (m_FadeTimer < Duration)
		{
			m_FadeTimer += Time.Delta;
			
			float alpha = (m_FadeTimer / Duration).Clamp(0.0f, 1.0f);
			
			foreach (var renderer in m_Renderers)
				renderer.Tint = renderer.Tint.WithAlpha(alpha);

			await Task.Frame();
		}

		m_IsFading = false;
	}

	private async Task AsyncFadeOut()
	{
		if (m_IsFading)
			return;
		
		m_IsFading = true;
		m_FadeTimer = 0;
		
		while (m_FadeTimer < Duration)
		{
			m_FadeTimer += Time.Delta;
			
			float alpha = (m_FadeTimer / Duration).Clamp(0.0f, 1.0f).Remap(0.0f, 1.0f, 1.0f, 0.0f);
			
			foreach (var renderer in m_Renderers)
				renderer.Tint = renderer.Tint.WithAlpha(alpha);

			await Task.Frame();
		}
		
		m_IsFading = false;
	}
	
	private async Task AsyncFadeOutAndDestroy()
	{
		await AsyncFadeOut();

		if (Networking.IsHost)
			DestroyGameObject();
	}
	
	[Rpc.Broadcast(NetFlags.HostOnly)]
	public void FadeIn()
	{
		_ = AsyncFadeIn();
	}
	
	[Rpc.Broadcast(NetFlags.HostOnly)]
	public void FadeOut()
	{
		_ = AsyncFadeOut();
	}
	
	[Rpc.Broadcast(NetFlags.HostOnly)]
	public void FadeOutAndDestroy()
	{
		_ = AsyncFadeOutAndDestroy();
	}
}
