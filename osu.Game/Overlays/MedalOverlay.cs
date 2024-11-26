// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.Notifications.WebSocket;
using osu.Game.Online.Notifications.WebSocket.Events;
using osu.Game.Users;

namespace osu.Game.Overlays
{
    public partial class MedalOverlay : OsuFocusedOverlayContainer
    {
        protected override string? PopInSampleName => null;
        protected override string? PopOutSampleName => null;

        public override bool IsPresent => base.IsPresent || Scheduler.HasPendingTasks;

        protected override void PopIn() => this.FadeIn();

        protected override void PopOut() => this.FadeOut();

        private readonly Queue<MedalAnimation> queuedMedals = new Queue<MedalAnimation>();

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private Container<Drawable> medalContainer = null!;
        private MedalAnimation? currentMedalDisplay;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            api.NotificationsClient.MessageReceived += handleMedalMessages;

            Add(medalContainer = new Container
            {
                RelativeSizeAxes = Axes.Both
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            OverlayActivationMode.BindValueChanged(_ => displayIfReady(), true);
        }

        private void handleMedalMessages(SocketMessage obj)
        {
            if (obj.Event != @"new")
                return;

            var data = obj.Data?.ToObject<NewPrivateNotificationEvent>();
            if (data == null || data.Name != @"user_achievement_unlock")
                return;

            var details = data.Details?.ToObject<UserAchievementUnlock>();
            if (details == null)
                return;

            var medal = new Medal
            {
                Name = details.Title,
                InternalName = details.Slug,
                Description = details.Description,
            };

            var medalAnimation = new MedalAnimation(medal);

            queuedMedals.Enqueue(medalAnimation);
            Logger.Log($"Queueing medal unlock for \"{medal.Name}\" ({queuedMedals.Count} to display)");

            Schedule(displayIfReady);
        }

        protected override bool OnClick(ClickEvent e)
        {
            dismissDisplayedMedal();
            loadNextMedal();
            return true;
        }

        public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Action == GlobalAction.Back)
            {
                dismissDisplayedMedal();
                loadNextMedal();
                return true;
            }

            return base.OnPressed(e);
        }

        private void dismissDisplayedMedal()
        {
            if (currentMedalDisplay?.IsLoaded == false)
                return;

            currentMedalDisplay?.Dismiss();
            currentMedalDisplay = null;
        }

        private void displayIfReady()
        {
            if (OverlayActivationMode.Value != OverlayActivation.All)
                return;

            if (currentMedalDisplay != null)
            {
                Show();
                return;
            }

            if (queuedMedals.Any())
            {
                Show();
                loadNextMedal();
            }
        }

        private void loadNextMedal()
        {
            if (currentMedalDisplay != null)
                return;

            if (!queuedMedals.TryDequeue(out currentMedalDisplay))
            {
                Logger.Log("All queued medals have been displayed!");
                Hide();
                return;
            }

            Logger.Log($"Preparing to display \"{currentMedalDisplay.Medal.Name}\"");
            LoadComponentAsync(currentMedalDisplay, m => medalContainer.Add(m));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (api.IsNotNull())
                api.NotificationsClient.MessageReceived -= handleMedalMessages;
        }
    }
}
