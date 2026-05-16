using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Content;

namespace XamarinPosed
{
    [Activity(Name = "xamarin.posed.Main", Label = "@string/app_name", MainLauncher = true)]
    public class Main : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_main);
        }
    }
}
