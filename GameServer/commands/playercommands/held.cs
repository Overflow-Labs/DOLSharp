namespace DOL.GS.Commands
{
	[CmdAttribute(
		"&held",
		ePrivLevel.Player,
		"List the cards in your hand. Use 'held g' to display faceup cards held by group members.",
		"/held <g>")]
	public class HeldCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
            if (args.Length == 2 && client.Player.Group != null)
                foreach (GamePlayer Groupee in client.Player.Group.GetPlayersInTheGroup())
                    if(Groupee != client.Player) CardMgr.Held(client, Groupee.Client);
            else
                CardMgr.Held(client, client);
		}
	}
}