﻿using AntDesign;
using AntDesign.ProLayout;
using Microsoft.AspNetCore.Components;
using NodeService.WebServer.Services.Auth;
using NodeService.WebServer.UI.Models;
using NodeService.WebServer.UI.Services;

namespace NodeService.WebServer.UI.Components;

public partial class RightContent
{
    private int _count = 0;
    private CurrentUser _currentUser = new();
    private NoticeIconData[] _events = { };
    private NoticeIconData[] _messages = { };
    private NoticeIconData[] _notifications = { };

    private List<AutoCompleteDataItem<string>> DefaultOptions { get; set; } = new()
    {
        new AutoCompleteDataItem<string>
        {
            Label = "umi ui",
            Value = "umi ui"
        },
        new AutoCompleteDataItem<string>
        {
            Label = "Pro Table",
            Value = "Pro Table"
        },
        new AutoCompleteDataItem<string>
        {
            Label = "Pro Layout",
            Value = "Pro Layout"
        }
    };

    public AvatarMenuItem[] AvatarMenuItems { get; set; } =
    {
        //new() { Key = "center", IconType = "user", Option = "个人中心"},
        //new() { Key = "setting", IconType = "setting", Option = "个人设置"},
        //new() { IsDivider = true },
        new() { Key = "logout", IconType = "logout", Option = "退出登录" }
    };

    [Inject] protected NavigationManager NavigationManager { get; set; }

    [Inject] protected IUserService UserService { get; set; }
    [Inject] protected IProjectService ProjectService { get; set; }
    [Inject] protected MessageService MessageService { get; set; }
    [Inject] protected LoginService LoginService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState).User;
        if (user.Identity.IsAuthenticated) _currentUser.Name = user.Identity.Name;
        await base.OnInitializedAsync();
        SetClassMap();
        var notices = await ProjectService.GetNoticesAsync();
        _notifications = notices.Where(x => x.Type == "notification").Cast<NoticeIconData>().ToArray();
        _messages = notices.Where(x => x.Type == "message").Cast<NoticeIconData>().ToArray();
        _events = notices.Where(x => x.Type == "event").Cast<NoticeIconData>().ToArray();
        _count = notices.Length;
    }

    protected void SetClassMap()
    {
        ClassMapper
            .Clear()
            .Add("right");
    }

    public async Task HandleSelectUser(MenuItem item)
    {
        switch (item.Key)
        {
            case "center":
                NavigationManager.NavigateTo("/account/center");
                break;
            case "setting":
                NavigationManager.NavigateTo("/account/settings");
                break;
            case "logout":
                await LoginService.LogoutAsync();
                break;
        }
    }

    public void HandleSelectLang(MenuItem item)
    {
    }

    public async Task HandleClear(string key)
    {
        switch (key)
        {
            case "notification":
                _notifications = new NoticeIconData[] { };
                break;
            case "message":
                _messages = new NoticeIconData[] { };
                break;
            case "event":
                _events = new NoticeIconData[] { };
                break;
        }

        await MessageService.Success($"清空了{key}");
    }

    public async Task HandleViewMore(string key)
    {
        await MessageService.Info("Click on view more");
    }
}