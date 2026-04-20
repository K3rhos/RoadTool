using Sandbox;

namespace RedSnail.RoadTool;

public static class SandboxUtility
{
	// Game.IsPlaying is broken right now on S&box, using LoadingScreen.IsVisible is a good alternative to tell if we're playing the game bcs this one is true when stuff init for the first time
	public static bool IsInPlayMode => LoadingScreen.IsVisible || Game.IsPlaying;
	
	
	
	/// <summary>
	/// Yes I know this is not intended to be used for notification purpose, but it does the job of notifying the user, so it's a nice work around
	/// until Facepunch give us a proper way to show editor notifications easily (Technically it's related to ToastManager class but only available on the editor side)
	/// </summary>
	public static void ShowEditorNotification(string _Text, int _Duration = 1500)
	{
		if (Application.Editor is null)
			return;

		// Dummy array (unused)
		Component[] components = [ Game.ActiveScene.Get<Component>() ];
		
		// Fire and forget a dummy async task of a specific duration to show a notification in bottom left corner of your screen
		_ = Application.Editor.ForEachAsync(components, _Text, (_, ct) => GameTask.Delay(_Duration, ct));
	}
}
