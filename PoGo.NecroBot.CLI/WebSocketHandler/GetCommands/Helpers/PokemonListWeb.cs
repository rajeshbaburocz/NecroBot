﻿#region using directives

using System.Collections.Generic;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.Service;
using POGOProtos.Data;
using POGOProtos.Inventory;
using POGOProtos.Settings.Master;

#endregion

namespace PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Helpers
{
    public class PokemonListWeb
    {
        private readonly List<Candy> _families;
        private readonly IEnumerable<PokemonSettings> _settings;
        public PokemonData Base;

        public PokemonListWeb(PokemonData data, List<Candy> family, IEnumerable<PokemonSettings> settings)
        {
            Base = data;
            _families = family;
            _settings = settings;
        }

        public double IvPerfection => PokemonInfo.CalculatePokemonPerfection(Base);
        public int FamilyCandies => PokemonInfo.GetCandy(Base, _families, _settings);
    }
}