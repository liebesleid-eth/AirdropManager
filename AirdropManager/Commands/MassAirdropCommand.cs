using Rocket.API;
using Rocket.Core.Logging;
using System;
using System.Collections.Generic;

namespace RestoreMonarchy.AirdropManager.Commands
{
    public class MassAirdropCommand : IRocketCommand
    {
        public void Execute(IRocketPlayer caller, params string[] command)
        {
            AirdropManagerPlugin.Instance.CallAirdrop(true);
            Logger.Log(AirdropManagerPlugin.Instance.Translate("SuccessMassAirdrop"), ConsoleColor.Yellow);
        }

        public string Help => "Calls in mass airdrop";

        public string Name => "massairdrop";

        public string Syntax => string.Empty;

        public List<string> Aliases => new List<string>();

        public List<string> Permissions => new List<string>() { "massairdrop" };

        public AllowedCaller AllowedCaller => AllowedCaller.Both;
    }
}
