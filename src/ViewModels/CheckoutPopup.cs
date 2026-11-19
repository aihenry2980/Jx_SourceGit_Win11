using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public abstract class CheckoutPopup : Popup
    {
        public override bool ShowCheckoutActions => true;
        public override double PopupWidth => 640;

        public sealed override Task<bool> Sure() => CheckoutAsync(false);
        public sealed override Task<bool> SureWithSubmodules() => CheckoutAsync(true);

        protected abstract Task<bool> CheckoutAsync(bool updateSubmodules);

        protected async Task<bool> UpdateSubmodulesAfterCheckoutAsync(Repository repo, CommandLog log, bool updateSubmodules)
        {
            if (!updateSubmodules)
                return true;

            ProgressDescription = App.Text("Checkout.UpdatingSubmodules");
            var succeeded = await new Commands.Submodule(repo.FullPath)
                .Use(log).UpdateAsync([], true, true, false);
            if (!succeeded)
                repo.SendNotification(App.Text("Checkout.SubmoduleUpdateFailed"), true);
            repo.RefreshSubmodules();
            return succeeded;
        }
    }
}
