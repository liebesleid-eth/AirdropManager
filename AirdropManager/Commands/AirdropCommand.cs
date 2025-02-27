﻿using Rocket.API;
using Rocket.Core.Logging;
using System;
using System.Collections.Generic;

namespace RestoreMonarchy.AirdropManager.Commands
{
    public class AirdropCommand : IRocketCommand
    {
        public void Execute(IRocketPlayer caller, params string[] command)
        {
            AirdropManagerPlugin.Instance.CallAirdrop(false);
            Logger.Log(AirdropManagerPlugin.Instance.Translate("SuccessAirdrop"), ConsoleColor.Yellow);
        }

        public string Help => "Calls in airdrop";

        public string Name => "airdrop";

        public string Syntax => string.Empty;

        public List<string> Aliases => new List<string>();

        public List<string> Permissions => new List<string>() { "airdrop" };

        public AllowedCaller AllowedCaller => AllowedCaller.Both;
    }
}
