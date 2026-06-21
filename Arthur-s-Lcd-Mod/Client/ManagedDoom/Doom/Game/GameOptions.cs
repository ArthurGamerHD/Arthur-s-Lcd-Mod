//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//



using System;
using ManagedDoom.Video;
using ManagedDoom.Audio;
using ManagedDoom.UserInput;

namespace ManagedDoom
{
    public sealed class GameOptions
    {
        private GameVersion gameVersion;
        private GameMode gameMode;
        private MissionPack missionPack;

        private Player[] players;
        private int consolePlayer;

        private int episode;
        private int map;
        private GameSkill skill;

        private bool demoPlayback;
        private bool netGame;

        private int deathmatch;
        private bool fastMonsters;
        private bool respawnMonsters;
        private bool noMonsters;

        private IntermissionInfo intermissionInfo;

        private DoomRandom random;

        private IVideo video;
        private ISound sound;
        private IMusic music;
        private IUserInput userInput;

        public GameOptions()
        {
            gameVersion = GameVersion.Version109;
            gameMode = GameMode.Commercial;
            missionPack = MissionPack.Doom2;

            players = new Player[Player.MaxPlayerCount];
            for (var i = 0; i < Player.MaxPlayerCount; i++)
            {
                players[i] = new Player(i);
            }
            players[0].InGame = true;
            consolePlayer = 0;

            episode = 1;
            map = 1;
            skill = GameSkill.Medium;

            demoPlayback = false;
            netGame = false;

            deathmatch = 0;
            fastMonsters = false;
            respawnMonsters = false;
            noMonsters = false;

            intermissionInfo = new IntermissionInfo();

            random = new DoomRandom();

            video = NullVideo.GetInstance();
            sound = NullSound.GetInstance();
            music = NullMusic.GetInstance();
            userInput = NullUserInput.GetInstance();
        }

        public GameOptions(CommandLineArgs args, GameContent content) : this()
        {
            if (args.solonet.Present)
            {
                netGame = true;
            }

            gameVersion = content.Wad.GameVersion;
            gameMode = content.Wad.GameMode;
            missionPack = content.Wad.MissionPack;
        }

        public GameVersion GameVersion
        {
            get { return gameVersion; }
            set { gameVersion = value; }
        }

        public GameMode GameMode
        {
            get { return gameMode; }
            set { gameMode = value; }
        }

        public MissionPack MissionPack
        {
            get { return missionPack; }
            set { missionPack = value; }
        }

        public Player[] Players
        {
            get { return players; }
        }

        public int ConsolePlayer
        {
            get { return consolePlayer; }
            set { consolePlayer = value; }
        }

        public int Episode
        {
            get { return episode; }
            set { episode = value; }
        }

        public int Map
        {
            get { return map; }
            set { map = value; }
        }

        public GameSkill Skill
        {
            get { return skill; }
            set { skill = value; }
        }

        public bool DemoPlayback
        {
            get { return demoPlayback; }
            set { demoPlayback = value; }
        }

        public bool NetGame
        {
            get { return netGame; }
            set { netGame = value; }
        }

        public int Deathmatch
        {
            get { return deathmatch; }
            set { deathmatch = value; }
        }

        public bool FastMonsters
        {
            get { return fastMonsters; }
            set { fastMonsters = value; }
        }

        public bool RespawnMonsters
        {
            get { return respawnMonsters; }
            set { respawnMonsters = value; }
        }

        public bool NoMonsters
        {
            get { return noMonsters; }
            set { noMonsters = value; }
        }

        public IntermissionInfo IntermissionInfo
        {
            get { return intermissionInfo; }
        }

        public DoomRandom Random
        {
            get { return random; }
        }

        public IVideo Video
        {
            get { return video; }
            set { video = value; }
        }

        public ISound Sound
        {
            get { return sound; }
            set { sound = value; }
        }

        public IMusic Music
        {
            get { return music; }
            set { music = value; }
        }

        public IUserInput UserInput
        {
            get { return userInput; }
            set { userInput = value; }
        }
    }
}
