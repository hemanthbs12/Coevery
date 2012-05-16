﻿using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Orchard.ContentManagement;
using Orchard.Core.Common.Models;
using Orchard.Core.Navigation.Models;
using Orchard.Core.Navigation.Services;
using Orchard.Core.Navigation.ViewModels;
using Orchard.Core.Title.Models;
using Orchard.Localization;
using Orchard.Mvc.Extensions;
using Orchard.UI;
using Orchard.UI.Notify;
using Orchard.UI.Navigation;
using Orchard.Utility;
using System;
using Orchard.Logging;

namespace Orchard.Core.Navigation.Controllers {
    [ValidateInput(false)]
    public class AdminController : Controller, IUpdateModel {
        private readonly IMenuService _menuService;
        private readonly INavigationManager _navigationManager;
        private readonly IMenuManager _menuManager;

        public AdminController(
            IOrchardServices orchardServices,
            IMenuService menuService,
            IMenuManager menuManager,
            INavigationManager navigationManager) {
            _menuService = menuService;
            _menuManager = menuManager;
            _navigationManager = navigationManager;
            
            Services = orchardServices;
            T = NullLocalizer.Instance;
            Logger = NullLogger.Instance;
        }

        public Localizer T { get; set; }
        public ILogger Logger { get; set; }
        public IOrchardServices Services { get; set; }

        public ActionResult Index(NavigationManagementViewModel model, int? menuId) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Not allowed to manage the main menu"))) {
                return new HttpUnauthorizedResult();
            }

            IEnumerable<TitlePart> menus = Services.ContentManager.Query<TitlePart, TitlePartRecord>().OrderBy(x => x.Title).ForType("Menu").List();

            if (!menus.Any()) {
                return RedirectToAction("Create", "Admin", new {area = "Contents", id = "Menu", returnUrl = Request.RawUrl});
            }

            IContent currentMenu = menuId == null
                ? menus.FirstOrDefault()
                : menus.FirstOrDefault(menu => menu.Id == menuId);

            if (currentMenu == null && menuId != null) { // incorrect menu id passed
                Services.Notifier.Error(T("Menu not found: {0}", menuId));
                return RedirectToAction("Index");
            }

            if (model == null) {
                model = new NavigationManagementViewModel();
            }

            if (model.MenuItemEntries == null || !model.MenuItemEntries.Any()) {
                model.MenuItemEntries = _menuService.GetMenu(currentMenu.Id).Select(CreateMenuItemEntries).OrderBy(menuPartEntry => menuPartEntry.Position, new FlatPositionComparer()).ToList();
            }

            model.MenuItemDescriptors = _menuManager.GetMenuItemTypes();
            model.Menus = menus;
            model.CurrentMenu = currentMenu;

            // need action name as this action is referenced from another action
            return View(model);
        }

        [HttpPost, ActionName("Index")]
        public ActionResult IndexPOST(IList<MenuItemEntry> menuItemEntries) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Couldn't manage the main menu")))
                return new HttpUnauthorizedResult();

            // See http://orchard.codeplex.com/workitem/17116
            if (menuItemEntries != null) {
                foreach (var menuItemEntry in menuItemEntries) {
                    MenuPart menuPart = _menuService.Get(menuItemEntry.MenuItemId);
                    menuPart.MenuPosition = menuItemEntry.Position;
                }
            }

            return RedirectToAction("Index");
        }

        private MenuItemEntry CreateMenuItemEntries(MenuPart menuPart) {
            return new MenuItemEntry {
                MenuItemId = menuPart.Id,
                IsMenuItem = menuPart.Is<MenuItemPart>(),
                Text = menuPart.MenuText,
                Position = menuPart.MenuPosition,
                Url = menuPart.Is<MenuItemPart>()
                              ? menuPart.As<MenuItemPart>().Url
                              : _navigationManager.GetUrl(null, Services.ContentManager.GetItemMetadata(menuPart).DisplayRouteValues),
                ContentItem = menuPart.ContentItem,
            };
        }

        public ActionResult Create() {
            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult Create(NavigationManagementViewModel model) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Couldn't manage the main menu")))
                return new HttpUnauthorizedResult();

            var menuPart = Services.ContentManager.New<MenuPart>("MenuItem");
            menuPart.OnMainMenu = true;
            menuPart.MenuText = model.NewMenuItem.Text;
            menuPart.MenuPosition = model.NewMenuItem.Position;
            if (string.IsNullOrEmpty(menuPart.MenuPosition))
                menuPart.MenuPosition = Position.GetNext(_navigationManager.BuildMenu("main"));

            var menuItem = menuPart.As<MenuItemPart>();
            menuItem.Url = model.NewMenuItem.Url;

            if (!ModelState.IsValid) {
                Services.TransactionManager.Cancel();
                return View("Index", model);
            }

            Services.ContentManager.Create(menuPart);

            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult Delete(int id) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Couldn't manage the main menu")))
                return new HttpUnauthorizedResult();

            MenuPart menuPart = _menuService.Get(id);

            if (menuPart != null) {
                if (menuPart.Is<MenuItemPart>())
                    _menuService.Delete(menuPart);
                else
                    menuPart.OnMainMenu = false;
            }

            return RedirectToAction("Index");
        }

        bool IUpdateModel.TryUpdateModel<TModel>(TModel model, string prefix, string[] includeProperties, string[] excludeProperties) {
            return TryUpdateModel(model, prefix, includeProperties, excludeProperties);
        }

        void IUpdateModel.AddModelError(string key, LocalizedString errorMessage) {
            ModelState.AddModelError(key, errorMessage.ToString());
        }

        public ActionResult CreateMenuItem(string id, int menuId, string returnUrl) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Couldn't manage the main menu")))
                return new HttpUnauthorizedResult();

            // create a new temporary menu item
            MenuPart menuPart = Services.ContentManager.New<MenuPart>(id);

            if (menuPart == null)
                return HttpNotFound();
            
            // load the menu
            var menu = Services.ContentManager.Get(menuId);

            if (menu == null)
                return HttpNotFound();
            
            try {
                // filter the content items for this specific menu
                menuPart.MenuPosition = Position.GetNext(_navigationManager.BuildMenu("main").Where(x => x.MenuId == menuId));
                
                dynamic model = Services.ContentManager.BuildEditor(menuPart);
                
                // Casting to avoid invalid (under medium trust) reflection over the protected View method and force a static invocation.
                return View((object)model);
            }
            catch (Exception exception) {
                Logger.Error(T("Creating menu item failed: {0}", exception.Message).Text);
                Services.Notifier.Error(T("Creating menu item failed: {0}", exception.Message));
                return this.RedirectLocal(returnUrl, () => RedirectToAction("Index"));
            }
        }

        [HttpPost, ActionName("CreateMenuItem")]
        public ActionResult CreateMenuItemPost(string id, int menuId, string returnUrl) {
            if (!Services.Authorizer.Authorize(Permissions.ManageMainMenu, T("Couldn't manage the main menu")))
                return new HttpUnauthorizedResult();

            MenuPart menuPart = Services.ContentManager.New<MenuPart>(id);

            if (menuPart == null)
                return HttpNotFound();

            // load the menu
            var menu = Services.ContentManager.Get(menuId);

            if (menu == null)
                return HttpNotFound();
            
            var model = Services.ContentManager.UpdateEditor(menuPart, this);

            menuPart.MenuPosition = Position.GetNext(_navigationManager.BuildMenu("main").Where(x => x.MenuId == menuId));
            menuPart.OnMainMenu = true;
            
            // the menu is the container for the menu item
            menuPart.As<CommonPart>().Container = menu;

            Services.ContentManager.Create(menuPart);

            if (!ModelState.IsValid) {
                Services.TransactionManager.Cancel();
                // Casting to avoid invalid (under medium trust) reflection over the protected View method and force a static invocation.
                return View((object)model);
            }

            Services.Notifier.Information(T("Your {0} has been added.", menuPart.TypeDefinition.DisplayName));

            return this.RedirectLocal(returnUrl, () => RedirectToAction("Index"));
        }
    }
}
