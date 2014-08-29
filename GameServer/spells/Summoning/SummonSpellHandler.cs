/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */

using System;
using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Pet summon spell handler
	/// 
	/// Spell.LifeDrainReturn is used for pet ID.
	///
	/// Spell.Value is used for hard pet level cap
	/// Spell.Damage is used to set pet level:
	/// less than zero is considered as a percent (0 .. 100+) of target level;
	/// higher than zero is considered as level value.
	/// Resulting value is limited by the Byte field type.
	/// </summary>
	public abstract class SummonSpellHandler : SpellHandler
	{
		private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Summon shoud always be duration based.
		/// </summary>
		public override bool ForceStartEffect {
			get { return true; }
		}
		
		protected GamePet m_pet = null;

		/// <summary>
		/// Is a summon of this pet silent (no message to caster, or ambient texts)?
		/// </summary>
		protected bool m_isSilent = false;

		public SummonSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		/// <summary>
		/// called after normal spell cast is completed and effect has to be started
		/// </summary>
		public override void FinishSpellCast(GameLiving target)
		{
			foreach (GamePlayer player in m_caster.GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
			{
				if (player != m_caster)
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameObject.Casting.CastsASpell", m_caster.GetName(0, true)), eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
			}

			m_caster.Mana -= PowerCost(target, true);

			base.FinishSpellCast(target);

			if (m_pet == null)
				return;

			if (Spell.Message1 == string.Empty)
			{
				if (m_isSilent == false)
				{
					MessageToCaster(String.Format("The {0} is now under your control.", m_pet.Name), eChatType.CT_Spell);
				}
			}
			else
			{
				MessageToCaster(Spell.Message1, eChatType.CT_Spell);
			}
		}

		#region ApplyEffectOnTarget Gets

		protected virtual void GetPetLocation(out int x, out int y, out int z, out ushort heading, out Region region)
		{
			Point2D point = Caster.GetPointFromHeading( Caster.Heading, 64 );
			x = point.X;
			y = point.Y;
			z = Caster.Z;
			heading = (ushort)((Caster.Heading + 2048) % 4096);
			region = Caster.CurrentRegion;
		}

		protected virtual GamePet GetGamePet(INpcTemplate template)
		{
			return Caster.CreateGamePet(template);
		}

		protected virtual IControlledBrain GetPetBrain(GameLiving owner)
		{
			return new ControlledNpcBrain(owner);
		}

		protected virtual void SetBrainToOwner(IControlledBrain brain)
		{
			Caster.SetControlledBrain(brain);
		}

		protected virtual byte GetPetLevel()
		{
			byte level;

			if (Spell.Damage < 0)
				level = (byte)(Caster.Level * Spell.Damage * -0.01);
			else
				level = (byte)Spell.Damage;

			if (level > Spell.Value)
				level = (byte)Spell.Value;

			return Math.Max((byte)1, level);
		}

		protected virtual void AddHandlers()
		{
			GameEventMgr.AddHandler(m_pet, GameLivingEvent.PetReleased, new DOLEventHandler(OnNpcReleaseCommand));
		}

		#endregion

		/// <summary>
		/// Apply effect on target or do spell action if non duration spell
		/// </summary>
		/// <param name="target">target that gets the effect</param>
		/// <param name="effectiveness">factor from 0..1 (0%-100%)</param>
		public override void ApplyEffectOnTarget(GameLiving target, double effectiveness)
		{
			INpcTemplate template = NpcTemplateMgr.GetTemplate(Spell.LifeDrainReturn);
			if (template == null)
			{
				if (log.IsWarnEnabled)
					log.WarnFormat("NPC template {0} not found! Spell: {1}", Spell.LifeDrainReturn, Spell.ToString());
				MessageToCaster("NPC template " + Spell.LifeDrainReturn + " not found!", eChatType.CT_System);
				return;
			}

			GameSpellEffect effect = CreateSpellEffect(target, effectiveness);

			IControlledBrain brain = GetPetBrain(Caster);
			m_pet = GetGamePet(template);
			//brain.WalkState = eWalkState.Stay;
			m_pet.SetOwnBrain(brain as AI.ABrain);

			int x, y, z;
			ushort heading;
			Region region;

			GetPetLocation(out x, out y, out z, out heading, out region);

			m_pet.X = x;
			m_pet.Y = y;
			m_pet.Z = z;
			m_pet.Heading = heading;
			m_pet.CurrentRegion = region;

			m_pet.CurrentSpeed = 0;
			m_pet.Realm = Caster.Realm;
			m_pet.Level = GetPetLevel();

			if (m_isSilent)
				m_pet.IsSilent = true;

			m_pet.AddToWorld();
			
			AddHandlers();

			SetBrainToOwner(brain);

			effect.Start(m_pet);

			Caster.OnPetSummoned(m_pet);
			
			//Check for buffs
			if (brain is ControlledNpcBrain)
			{
				try
				{
					(brain as ControlledNpcBrain).CheckSpells(StandardMobBrain.eCheckSpellType.Defensive);
				}
				catch (Exception ex)
				{
					if(log.IsErrorEnabled)
						log.ErrorFormat("Error while Summoning and Checking Pet Spells : {0}", ex);
				}
				
				try
				{
					(brain as ControlledNpcBrain).CheckAbilities();
				}
				catch (Exception ex)
				{
					if(log.IsErrorEnabled)
						log.ErrorFormat("Error while Summoning and Checking Pet Abilities : {0}", ex);
				}				
			}

		}

		public override int CalculateSpellResistChance(GameLiving target)
		{
			return 0;
		}

		/// <summary>
		/// When an applied effect expires.
		/// Duration spells only.
		/// </summary>
		/// <param name="effect">The expired effect</param>
		/// <param name="noMessages">true, when no messages should be sent to player and surrounding</param>
		/// <returns>immunity duration in milliseconds</returns>
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{			
			if(effect.Owner.IsAlive)
			{
				effect.Owner.Health = 0; // to send proper remove packet
				effect.Owner.Die(null);
			}
			
			return 0;
		}

		/// <summary>
		/// Called when owner release NPC
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		protected virtual void OnNpcReleaseCommand(DOLEvent e, object sender, EventArgs arguments)
		{
            if (!(sender is GameNPC) || !((sender as GameNPC).Brain is IControlledBrain))
                return;

            GameNPC pet = sender as GameNPC;
            IControlledBrain brain = pet.Brain as IControlledBrain;
            GameLiving living = brain.Owner;
            living.SetControlledBrain(null);

            GameEventMgr.RemoveHandler(pet, GameLivingEvent.PetReleased, new DOLEventHandler(OnNpcReleaseCommand));

            GameSpellEffect effect = SpellHelper.FindEffectOnTarget(pet, this);
            if (effect != null)
                effect.Cancel(false);
		}

		/// <summary>
		/// Delve Info
		/// </summary>
		public override IList<string> DelveInfo
		{
			get
			{
				var list = new List<string>();

				list.Add("Function: " + (Spell.SpellType == "" ? "(not implemented)" : Spell.SpellType));
				list.Add(" "); //empty line
				list.Add(Spell.Description);
				list.Add(" "); //empty line
				if (Spell.InstrumentRequirement != 0)
					list.Add("Instrument require: " + GlobalConstants.InstrumentTypeToName(Spell.InstrumentRequirement));
				list.Add("Target: " + Spell.Target);
				if (Spell.Range != 0)
					list.Add("Range: " + Spell.Range);
				if (Spell.Duration >= ushort.MaxValue * 1000)
					list.Add("Duration: Permanent.");
				else if (Spell.Duration > 60000)
					list.Add(string.Format("Duration: {0}:{1} min", Spell.Duration / 60000, (Spell.Duration % 60000 / 1000).ToString("00")));
				else if (Spell.Duration != 0)
					list.Add("Duration: " + (Spell.Duration / 1000).ToString("0' sec';'Permanent.';'Permanent.'"));
				if (Spell.Frequency != 0)
					list.Add("Frequency: " + (Spell.Frequency * 0.001).ToString("0.0"));
				if (Spell.Power != 0)
					list.Add("Power cost: " + Spell.Power.ToString("0;0'%'"));
				list.Add("Casting time: " + (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'"));
				if (Spell.RecastDelay > 60000)
					list.Add("Recast time: " + (Spell.RecastDelay / 60000).ToString() + ":" + (Spell.RecastDelay % 60000 / 1000).ToString("00") + " min");
				else if (Spell.RecastDelay > 0)
					list.Add("Recast time: " + (Spell.RecastDelay / 1000).ToString() + " sec");
				if (Spell.Concentration != 0)
					list.Add("Concentration cost: " + Spell.Concentration);
				if (Spell.Radius != 0)
					list.Add("Radius: " + Spell.Radius);
				if (Spell.DamageType != eDamageType.Natural)
					list.Add("Damage: " + GlobalConstants.DamageTypeToName(Spell.DamageType));

				return list;
			}
		}
	}
}
