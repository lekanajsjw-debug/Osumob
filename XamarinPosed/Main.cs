using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using AndroidX.AppCompat.AppCompatResources;

namespace XamarinPosed
{
    [Activity(Name = "xamarin.posed.Main", Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public partial class Main : AndroidX.AppCompat.AppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Microsoft.Maui.ApplicationModel.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);
        }
    }
}
