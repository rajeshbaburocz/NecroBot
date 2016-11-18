﻿using PoGo.NecroBot.Logic.Tasks;
using POGOProtos.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PoGo.NecroBot.Logic.State
{
    public class BotSwitcherState : IState
    {
        private PokemonId pokemonToCatch;
        public BotSwitcherState(PokemonId pokemon)
        {
            this.pokemonToCatch = pokemon;
        }
        public async Task<IState> Execute(ISession session, CancellationToken cancellationToken)
        {
            await session.Client.Player.UpdatePlayerLocation(session.Client.CurrentLatitude, session.Client.CurrentLongitude, session.Client.CurrentAltitude, 10);
            await Task.Delay(1234, cancellationToken);
            await CatchNearbyPokemonsTask.Execute(session, cancellationToken, this.pokemonToCatch);
            await CatchLurePokemonsTask.Execute(session, cancellationToken);
            return new InfoState();
        }
    }
}
